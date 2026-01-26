using System;
using System.Windows;
using System.Windows.Controls;
using MeasureControl.Models;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services
{
    #region Dialog Service

    /// <summary>
    /// 对话框服务接口
    /// </summary>
    public interface IDialogService
    {
        string ShowRenameDialog(string currentName, string title = "重命名");
        MessageBoxResult ShowConfirmDialog(string message, string title = "确认");
        bool ShowConfirmationDialog(string message, string title = "确认");
        void ShowErrorDialog(string message, string title = "错误");
        void ShowInfoDialog(string message, string title = "信息");
        void ShowWarningDialog(string message, string title = "警告");
        IcdMappingItem ShowAddIcdMappingDialog(System.Collections.ObjectModel.ObservableCollection<string> availableIcdTabels, System.Collections.ObjectModel.ObservableCollection<IcdFrameItem> availableFrames);
    }

    #endregion

    #region Drag Drop Service

    /// <summary>
    /// 拖放服务接口
    /// </summary>
    public interface IDragDropService
    {
        event EventHandler<DropPxiChassisArgs> PxiChassisDropped;
        event EventHandler RefreshRequested;

        void Initialize(FrameworkElement mainGrid, Border pxiSourceBorder2722, Border pxiSourceBorder2519);
        void UpdateChassisList(System.Collections.ObjectModel.ObservableCollection<Models.ChassisModel> chassisList);
        void StartPxiChassisDrag(FrameworkElement source);
        void HandlePxiChassisDragEnter(DragEventArgs e);
        void HandlePxiChassisDragLeave(DragEventArgs e);
        void HandlePxiChassisDrop(DragEventArgs e);
        (int Row, int Column)? FindNextAvailablePosition();
        void HighlightChassisArea(bool highlight);
        void HighlightSingleChassis(int row, int column, bool highlight);
        void HighlightPxiSource(Border targetBorder, bool highlight, string color = "");
    }

    #endregion

    #region Window Manager Service

    /// <summary>
    /// 窗口管理器服务接口
    /// </summary>
    public interface IWindowManagerService
    {
        /// <summary>
        /// 最大化窗口
        /// </summary>
        /// <param name="window">要最大化的窗口</param>
        void MaximizeWindow(Window window);

        /// <summary>
        /// 最小化窗口（隐藏但保留导航按钮）
        /// </summary>
        /// <param name="window">要最小化的窗口</param>
        void MinimizeWindow(Window window);

        /// <summary>
        /// 关闭窗口（释放内容）
        /// </summary>
        /// <param name="window">要关闭的窗口</param>
        void CloseWindow(Window window);

        /// <summary>
        /// 恢复窗口到正常状态
        /// </summary>
        /// <param name="window">要恢复的窗口</param>
        void RestoreWindow(Window window);

        /// <summary>
        /// 切换窗口最大化状态
        /// </summary>
        /// <param name="window">要切换的窗口</param>
        void ToggleMaximizeWindow(Window window);

        /// <summary>
        /// 检查窗口是否最大化
        /// </summary>
        /// <param name="window">要检查的窗口</param>
        /// <returns>是否最大化</returns>
        bool IsMaximized(Window window);

        /// <summary>
        /// 检查窗口是否最小化
        /// </summary>
        /// <param name="window">要检查的窗口</param>
        /// <returns>是否最小化</returns>
        bool IsMinimized(Window window);

        /// <summary>
        /// 最小化主窗口
        /// </summary>
        void MinimizeMainWindow();

        /// <summary>
        /// 切换主窗口最大化状态
        /// </summary>
        void ToggleMaximizeMainWindow();

        /// <summary>
        /// 关闭主窗口
        /// </summary>
        void CloseMainWindow();

        /// <summary>
        /// 窗口状态改变事件
        /// </summary>
        event EventHandler<WindowStateChangedEventArgs> WindowStateChanged;

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        event EventHandler<WindowClosedEventArgs> WindowClosed;
    }

    /// <summary>
    /// 窗口状态改变事件参数
    /// </summary>
    public class WindowStateChangedEventArgs : EventArgs
    {
        public Window Window { get; set; }
        public WindowState OldState { get; set; }
        public WindowState NewState { get; set; }
    }

    /// <summary>
    /// 窗口关闭事件参数
    /// </summary>
    public class WindowClosedEventArgs : EventArgs
    {
        public Window Window { get; set; }
        public bool ContentReleased { get; set; }
    }

    #endregion

    #region Project Save State Service

    /// <summary>
    /// 项目保存状态管理服务接口
    /// </summary>
    public interface IProjectSaveStateService
    {
        /// <summary>
        /// 项目是否有未保存的更改
        /// </summary>
        bool HasUnsavedChanges { get; }

        /// <summary>
        /// 项目保存状态改变事件
        /// </summary>
        event EventHandler<bool> SaveStateChanged;

        /// <summary>
        /// 标记项目为已修改
        /// </summary>
        void MarkAsModified();

        /// <summary>
        /// 标记项目为已保存
        /// </summary>
        void MarkAsSaved();

        /// <summary>
        /// 重置保存状态
        /// </summary>
        void Reset();

        /// <summary>
        /// 检查是否可以安全关闭项目
        /// </summary>
        /// <returns>如果可以安全关闭返回true，否则返回false</returns>
        bool CanCloseSafely();
    }

    #endregion

    #region Project Tree Service

    /// <summary>
    /// 项目树服务接口
    /// </summary>
    public interface IProjectTreeService
    {
        /// <summary>
        /// 在项目中重命名PXI机箱
        /// </summary>
        /// <param name="project">项目</param>
        /// <param name="oldName">旧名称</param>
        /// <param name="newName">新名称</param>
        void RenamePxiChassisInProject(System.Collections.ObjectModel.ObservableCollection<ProjectItem> project, string oldName, string newName);

        /// <summary>
        /// 添加PXI机箱到项目
        /// </summary>
        /// <param name="project">项目</param>
        /// <param name="chassis">机箱</param>
        void AddPxiChassisToProject(System.Collections.ObjectModel.ObservableCollection<ProjectItem> project, ChassisModel chassis);

        /// <summary>
        /// 从项目移除PXI机箱
        /// </summary>
        /// <param name="project">项目</param>
        /// <param name="chassisName">机箱名称</param>
        void RemovePxiChassisFromProject(System.Collections.ObjectModel.ObservableCollection<ProjectItem> project, string chassisName);
    }

    #endregion

    #region PXI Chassis Service

    /// <summary>
    /// PXI机箱服务接口
    /// </summary>
    public interface IPxiChassisService
    {
        /// <summary>
        /// 获取所有机箱
        /// </summary>
        /// <returns>机箱列表</returns>
        System.Collections.ObjectModel.ObservableCollection<ChassisModel> GetAllChassis();

        /// <summary>
        /// 添加设备到机箱
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="device">设备</param>
        void AddDeviceToChassis(string chassisName, Models.Devices.DeviceBase device);

        /// <summary>
        /// 从机箱移除设备
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="deviceId">设备ID</param>
        void RemoveDeviceFromChassis(string chassisName, string deviceId);

        /// <summary>
        /// 保存机箱数据到项目
        /// </summary>
        /// <param name="chassisData">机箱数据</param>
        void SaveChassisData(System.Collections.ObjectModel.ObservableCollection<Models.ChassisModel> chassisData);

        /// <summary>
        /// 加载机箱数据到服务
        /// </summary>
        /// <param name="chassisData">机箱数据</param>
        void LoadChassisData(System.Collections.ObjectModel.ObservableCollection<Models.ChassisModel> chassisData);

        /// <summary>
        /// 检查位置是否被占用
        /// </summary>
        /// <param name="row">行</param>
        /// <param name="column">列</param>
        /// <returns>是否被占用</returns>
        bool IsPositionOccupied(int row, int column);

        /// <summary>
        /// 生成唯一名称（建议名称，不占用名称）
        /// </summary>
        /// <param name="baseName">基础名称</param>
        /// <returns>唯一名称建议</returns>
        string GenerateUniqueName(string baseName);
        
        /// <summary>
        /// 占用机箱名称（在用户确认添加机箱时调用）
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        void ReserveChassisName(string chassisName);

        /// <summary>
        /// 确保机箱的 ChassisDevice 已创建并注册
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="chassisModel">机箱型号</param>
        /// <returns>ChassisDevice 实例</returns>
        ChassisDevice EnsureChassisDevice(string chassisName, string chassisModel);

        /// <summary>
        /// 添加机箱
        /// </summary>
        /// <param name="chassis">机箱</param>
        bool AddChassis(ChassisModel chassis);

        /// <summary>
        /// 更新机箱名称
        /// </summary>
        /// <param name="chassisId">机箱ID</param>
        /// <param name="newName">新名称</param>
        bool UpdateChassisName(string chassisId, string newName);

        /// <summary>
        /// 移除机箱
        /// </summary>
        /// <param name="chassisIdOrName">机箱ID或名称</param>
        bool RemoveChassis(string chassisIdOrName);

        /// <summary>
        /// 获取机箱设备
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>设备列表</returns>
        System.Collections.Generic.List<Models.Devices.DeviceBase> GetChassisDevices(string chassisName);

        /// <summary>
        /// 根据名称获取机箱
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>机箱</returns>
        ChassisModel GetChassisByName(string chassisName);

        /// <summary>
        /// 检查机箱是否存在
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>是否存在</returns>
        bool ChassisExists(string chassisName);

        /// <summary>
        /// 重命名机箱
        /// </summary>
        /// <param name="oldName">旧名称</param>
        /// <param name="newName">新名称</param>
        bool RenameChassis(string oldName, string newName);

        /// <summary>
        /// 为机箱内的板卡生成唯一名称
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="device">设备对象（用于获取ParentNode）</param>
        /// <returns>唯一的板卡名称</returns>
        string GenerateUniqueCardName(string chassisName, DeviceBase device);

        /// <summary>
        /// 重命名板卡
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="deviceId">设备ID</param>
        /// <param name="newCardName">新的板卡名称</param>
        /// <returns>是否成功</returns>
        bool RenameCard(string chassisName, string deviceId, string newCardName);

        /// <summary>
        /// 验证板卡名称在机箱内是否唯一
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="cardName">板卡名称</param>
        /// <param name="excludeDeviceId">排除的设备ID（用于重命名时排除自身）</param>
        /// <returns>是否唯一</returns>
        bool ValidateCardName(string chassisName, string cardName, string excludeDeviceId = null);

        /// <summary>
        /// 开始UI交互操作（抑制修改事件触发）
        /// </summary>
        void BeginUIInteraction();

        /// <summary>
        /// 结束UI交互操作（恢复修改事件触发）
        /// </summary>
        void EndUIInteraction();

        /// <summary>
        /// 获取下一个可用位置
        /// </summary>
        /// <returns>位置</returns>
        (int Row, int Column)? GetNextAvailablePosition();

        /// <summary>
        /// 通过设备ID查找设备
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>设备对象</returns>
        DeviceBase GetDeviceById(string deviceId);

        /// <summary>
        /// 更新设备的CardConfigData（同步到服务中的设备实例）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cardConfig">新的配置数据</param>
        /// <returns>是否成功</returns>
        bool UpdateDeviceCardConfig(string deviceId, Models.CardConfigDataBase cardConfig);
    }

    #endregion

    #region Chassis Connection Service

    /// <summary>
    /// 机箱连接服务接口
    /// </summary>
    public interface IChassisConnectionService
    {
        /// <summary>
        /// 获取所有连接
        /// </summary>
        /// <returns>连接列表</returns>
        System.Collections.ObjectModel.ObservableCollection<ChassisConnection> GetAllConnections();

        /// <summary>
        /// 添加连接
        /// </summary>
        /// <param name="connection">连接</param>
        bool AddConnection(ChassisConnection connection);

        /// <summary>
        /// 移除连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        bool RemoveConnection(string connectionId);

        /// <summary>
        /// 清除所有连接
        /// </summary>
        void ClearConnections();

        /// <summary>
        /// 清除所有连接（别名方法）
        /// </summary>
        void ClearAllConnections();

        /// <summary>
        /// 获取连接线
        /// </summary>
        /// <returns>连接线列表</returns>
        System.Collections.Generic.List<ConnectionLine> GetConnectionLines();

        /// <summary>
        /// 检查两个机箱是否已连接
        /// </summary>
        /// <param name="chassis1">机箱1</param>
        /// <param name="chassis2">机箱2</param>
        /// <returns>是否已连接</returns>
        bool AreChassisConnected(string chassis1, string chassis2);

        /// <summary>
        /// 根据机箱获取连接
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>连接列表</returns>
        System.Collections.Generic.List<ChassisConnection> GetConnectionsByChassis(string chassisName);

        /// <summary>
        /// 检查机箱是否有连接
        /// </summary>
        /// <param name="chassisId">机箱ID</param>
        /// <returns>是否有连接</returns>
        bool HasChassisConnections(string chassisId);

        /// <summary>
        /// 重命名现有连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="newName">新名称</param>
        /// <returns>是否重命名成功</returns>
        bool RenameConnection(string connectionId, string newName);

        /// <summary>
        /// 检查连接名称是否已被占用
        /// </summary>
        /// <param name="name">名称</param>
        /// <param name="excludeConnectionId">需要排除的连接ID</param>
        /// <returns>是否已存在</returns>
        bool IsConnectionNameInUse(string name, string excludeConnectionId = null);
    }

    #endregion
}
