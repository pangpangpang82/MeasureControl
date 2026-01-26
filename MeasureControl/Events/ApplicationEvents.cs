using System;
using System.Collections.Generic;
using System.Windows;
using Prism.Events;
using MeasureControl.Models;

namespace MeasureControl.Events
{
    #region Navigation Events

    /// <summary>
    /// 添加导航按钮事件
    /// </summary>
    public class AddNavigationButtonEvent : PubSubEvent<string>
    {
    }

    /// <summary>
    /// 清空导航按钮事件
    /// </summary>
    public class ClearNavigationButtonsEvent : PubSubEvent
    {
    }

    /// <summary>
    /// 隐藏当前页面事件（最小化按钮在嵌入模式下使用）
    /// </summary>
    public class HideCurrentPageEvent : PubSubEvent<HideCurrentPageEventArgs>
    {
    }

    /// <summary>
    /// 隐藏当前页面事件参数
    /// </summary>
    public class HideCurrentPageEventArgs
    {
        /// <summary>
        /// 是否是最小化操作（true=最小化，false=浮动）
        /// </summary>
        public bool IsMinimize { get; set; } = true;
    }

    /// <summary>
    /// 释放当前页面事件（关闭按钮使用）
    /// 支持传递页面名称参数，如果为null则释放当前激活的页面
    /// </summary>
    public class ReleaseCurrentPageEvent : PubSubEvent<string>
    {
    }

    #endregion

    #region Floating Window Events

    /// <summary>
    /// 页面浮动事件
    /// </summary>
    public class PageFloatedEvent : PubSubEvent<PageFloatedEventArgs>
    {
    }

    /// <summary>
    /// 页面浮动事件参数
    /// </summary>
    public class PageFloatedEventArgs
    {
        /// <summary>
        /// 浮动的页面名称
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 页面嵌入事件
    /// </summary>
    public class PageEmbeddedEvent : PubSubEvent<PageEmbeddedEventArgs>
    {
    }

    /// <summary>
    /// 页面嵌入事件参数
    /// </summary>
    public class PageEmbeddedEventArgs
    {
        /// <summary>
        /// 嵌入的页面名称
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 浮动窗口最小化事件
    /// </summary>
    public class FloatingWindowMinimizedEvent : PubSubEvent<FloatingWindowMinimizedEventArgs>
    {
    }

    /// <summary>
    /// 浮动窗口最小化事件参数
    /// </summary>
    public class FloatingWindowMinimizedEventArgs
    {
        /// <summary>
        /// 浮动窗口的 PageKey（唯一标识符）
        /// </summary>
        public string PageKey { get; set; }

        /// <summary>
        /// 页面类型名称
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 浮动窗口恢复事件（从最小化状态恢复）
    /// </summary>
    public class FloatingWindowRestoredEvent : PubSubEvent<FloatingWindowRestoredEventArgs>
    {
    }

    /// <summary>
    /// 浮动窗口恢复事件参数
    /// </summary>
    public class FloatingWindowRestoredEventArgs
    {
        /// <summary>
        /// 浮动窗口的 PageKey（唯一标识符）
        /// </summary>
        public string PageKey { get; set; }

        /// <summary>
        /// 页面类型名称
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 浮动窗口激活事件（窗口获得焦点时触发）
    /// </summary>
    public class FloatingWindowActivatedEvent : PubSubEvent<FloatingWindowActivatedEventArgs>
    {
    }

    /// <summary>
    /// 浮动窗口激活事件参数
    /// </summary>
    public class FloatingWindowActivatedEventArgs
    {
        /// <summary>
        /// 浮动窗口的 PageKey（唯一标识符）
        /// </summary>
        public string PageKey { get; set; }

        /// <summary>
        /// 页面类型名称
        /// </summary>
        public string PageName { get; set; }
    }

    #endregion

    #region PXI Chassis Events

    /// <summary>
    /// 添加PXI机箱事件
    /// </summary>
    public class AddPxiChassisEvent : PubSubEvent<string>
    {
    }

    /// <summary>
    /// 删除PXI机箱事件
    /// </summary>
    public class DeletePxiChassisEvent : PubSubEvent<string>
    {
    }

    /// <summary>
    /// 重命名PXI机箱事件
    /// </summary>
    public class RenamePxiChassisEvent : PubSubEvent<RenamePxiChassisEventArgs>
    {
    }

    /// <summary>
    /// 重命名PXI机箱事件参数
    /// </summary>
    public class RenamePxiChassisEventArgs
    {
        public string ChassisId { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
    }

    /// <summary>
    /// 设置机箱名称事件
    /// </summary>
    public class SetChassisNameEvent : PubSubEvent<string>
    {
    }

    /// <summary>
    /// 机箱选择事件
    /// </summary>
    public class PxiChassisSelectedEvent : PubSubEvent<PxiChassisSelectedEventArgs>
    {
    }

    /// <summary>
    /// 机箱选择事件参数
    /// </summary>
    public class PxiChassisSelectedEventArgs
    {
        /// <summary>
        /// 选中的机箱名称
        /// </summary>
        public string ChassisName { get; set; }

        /// <summary>
        /// 选中的机箱ID
        /// </summary>
        public string ChassisId { get; set; }
    }

    #endregion

    #region Device Events

    /// <summary>
    /// 设备修改事件
    /// </summary>
    public class DeviceModifiedEvent : PubSubEvent<DeviceModifiedEventArgs>
    {
    }

    /// <summary>
    /// 设备修改事件参数
    /// </summary>
    public class DeviceModifiedEventArgs
    {
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName { get; set; }

        /// <summary>
        /// 修改类型：Add, Delete, Update
        /// </summary>
        public string ModificationType { get; set; }

        /// <summary>
        /// 设备信息
        /// </summary>
        public string DeviceInfo { get; set; }

        /// <summary>
        /// 设备对象
        /// </summary>
        public Models.Devices.DeviceBase Device { get; set; }
    }

    /// <summary>
    /// 设备点击事件
    /// </summary>
    public class DeviceClickedEvent : PubSubEvent<DeviceClickedEventArgs>
    {
    }

    /// <summary>
    /// 设备点击事件参数
    /// </summary>
    public class DeviceClickedEventArgs
    {
        /// <summary>
        /// 设备对象（包含所有设备信息）
        /// </summary>
        public Models.Devices.DeviceBase Device { get; set; }

        /// <summary>
        /// 设备类型
        /// </summary>
        public string DeviceType { get; set; }

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// 制造商
        /// </summary>
        public string Manufacturer { get; set; }

        /// <summary>
        /// 型号
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 连接方式
        /// </summary>
        public string ConnectionMethod { get; set; }

        /// <summary>
        /// 父节点
        /// </summary>
        public string ParentNode { get; set; }

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Details { get; set; }
    }

    #endregion

    #region Chassis Connection Events

    /// <summary>
    /// 机箱连接加载事件
    /// </summary>
    public class ChassisConnectionsLoadEvent : PubSubEvent<ChassisConnectionsLoadEventArgs>
    {
    }

    /// <summary>
    /// 机箱连接加载事件参数
    /// </summary>
    public class ChassisConnectionsLoadEventArgs
    {
        public System.Collections.Generic.List<Models.ChassisConnection> Connections { get; set; }
    }

    /// <summary>
    /// 机箱连接请求事件
    /// </summary>
    public class ChassisConnectionsRequestEvent : PubSubEvent<ChassisConnectionsRequestEventArgs>
    {
    }

    /// <summary>
    /// 机箱连接请求事件参数
    /// </summary>
    public class ChassisConnectionsRequestEventArgs
    {
        public System.Collections.Generic.List<Models.ChassisConnection> Connections { get; set; }
    }

    /// <summary>
    /// 连接线请求事件
    /// </summary>
    public class ConnectionLinesRequestEvent : PubSubEvent<ConnectionLinesRequestEventArgs>
    {
    }

    /// <summary>
    /// 连接线请求事件参数
    /// </summary>
    public class ConnectionLinesRequestEventArgs
    {
        public System.Collections.Generic.List<Models.ConnectionLine> ConnectionLines { get; set; }
    }

    /// <summary>
    /// 连接线加载事件
    /// </summary>
    public class ConnectionLinesLoadEvent : PubSubEvent<ConnectionLinesLoadEventArgs>
    {
    }

    /// <summary>
    /// 通道配置表数据请求事件
    /// </summary>
    public class ChannelTabelItemsRequestEvent : PubSubEvent<ChannelTabelItemsRequestEventArgs>
    {
    }

    /// <summary>
    /// 通道配置表数据请求事件参数
    /// </summary>
    public class ChannelTabelItemsRequestEventArgs
    {
        public Dictionary<string, List<ChannelTabelItem>> ChannelTabelItems { get; set; }
    }

    /// <summary>
    /// 通道配置表数据加载事件
    /// </summary>
    public class ChannelTabelItemsLoadEvent : PubSubEvent<ChannelTabelItemsLoadEventArgs>
    {
    }

    /// <summary>
    /// 通道配置表数据加载事件参数
    /// </summary>
    public class ChannelTabelItemsLoadEventArgs
    {
        public Dictionary<string, List<ChannelTabelItem>> ChannelTabelItems { get; set; }
    }

    /// <summary>
    /// 信号配置表数据请求事件
    /// </summary>
    public class SignalTabelItemsRequestEvent : PubSubEvent<SignalTabelItemsRequestEventArgs>
    {
    }

    /// <summary>
    /// 信号配置表数据请求事件参数
    /// </summary>
    public class SignalTabelItemsRequestEventArgs
    {
        public Dictionary<string, List<SignalConfigItem>> SignalTabelItems { get; set; }
    }

    /// <summary>
    /// 信号配置表数据加载事件
    /// </summary>
    public class SignalTabelItemsLoadEvent : PubSubEvent<SignalTabelItemsLoadEventArgs>
    {
    }

    /// <summary>
    /// 信号配置表数据加载事件参数
    /// </summary>
    public class SignalTabelItemsLoadEventArgs
    {
        public Dictionary<string, List<SignalConfigItem>> SignalTabelItems { get; set; }
    }

    /// <summary>
    /// 通讯信号配置表数据请求事件
    /// </summary>
    public class IcdMappingItemsRequestEvent : PubSubEvent<IcdMappingItemsRequestEventArgs>
    {
    }

    /// <summary>
    /// 通讯信号配置表数据请求事件参数
    /// </summary>
    public class IcdMappingItemsRequestEventArgs
    {
        public Dictionary<string, List<IcdMappingItem>> SignalTabelItems { get; set; }
    }

    /// <summary>
    /// ICD映射添加事件
    /// </summary>
    public class IcdMappingAddedEvent : PubSubEvent<IcdMappingItem>
    {
    }

    /// <summary>
    /// ICD配置表数据请求事件
    /// </summary>
    public class IcdTabelItemsRequestEvent : PubSubEvent<IcdTabelItemsRequestEventArgs>
    {
    }

    /// <summary>
    /// ICD配置表数据请求事件参数
    /// </summary>
    public class IcdTabelItemsRequestEventArgs
    {
        public Dictionary<string, List<Models.IcdFrameItem>> IcdTabelItems { get; set; }
        public string TestTaskName { get; set; }
        public string ConfigTabelName { get; set; }
    }

    /// <summary>
    /// 标定数据请求事件
    /// </summary>
    public class CalibrationRecordsRequestEvent : PubSubEvent<CalibrationRecordsRequestEventArgs>
    {
    }

    /// <summary>
    /// 标定数据请求事件参数
    /// </summary>
    public class CalibrationRecordsRequestEventArgs
    {
        public Dictionary<string, ChannelCalibrationRecord> CalibrationRecords { get; set; }
    }

    /// <summary>
    /// 标定数据加载事件
    /// </summary>
    public class CalibrationRecordsLoadEvent : PubSubEvent<CalibrationRecordsLoadEventArgs>
    {
    }

    /// <summary>
    /// 标定数据加载事件参数
    /// </summary>
    public class CalibrationRecordsLoadEventArgs
    {
        public Dictionary<string, ChannelCalibrationRecord> CalibrationRecords { get; set; }
    }

    /// <summary>
    /// ICD配置表数据加载事件
    /// </summary>
    public class IcdTabelItemsLoadEvent : PubSubEvent<IcdTabelItemsLoadEventArgs>
    {
    }

    /// <summary>
    /// ICD配置表数据加载事件参数
    /// </summary>
    public class IcdTabelItemsLoadEventArgs
    {
        public string TestTaskName { get; set; }
        public string ConfigTabelName { get; set; }
    }

    /// <summary>
    /// 连接线加载事件参数
    /// </summary>
    public class ConnectionLinesLoadEventArgs
    {
        public System.Collections.Generic.List<Models.ConnectionLine> ConnectionLines { get; set; }
    }

    #endregion

    #region Project Modification Events

    /// <summary>
    /// 项目修改事件
    /// </summary>
    public class ProjectModifiedEvent : PubSubEvent<ProjectModifiedEventArgs>
    {
    }

    /// <summary>
    /// 项目修改事件参数
    /// </summary>
    public class ProjectModifiedEventArgs
    {
        /// <summary>
        /// 修改类型：Connection, Chassis, Device, etc.
        /// </summary>
        public string ModificationType { get; set; }

        /// <summary>
        /// 修改描述
        /// </summary>
        public string Description { get; set; }
    }

    #endregion

    #region Window Events

    /// <summary>
    /// 窗口关闭事件
    /// </summary>
    public class WindowClosingEvent : PubSubEvent<WindowClosingEventArgs>
    {
    }

    /// <summary>
    /// 窗口关闭事件参数
    /// </summary>
    public class WindowClosingEventArgs
    {
        /// <summary>
        /// 关闭的窗口
        /// </summary>
        public Window Window { get; set; }

        /// <summary>
        /// 是否释放内容
        /// </summary>
        public bool ReleaseContent { get; set; }

        /// <summary>
        /// 要释放的页面名称（用于浮动窗口关闭时指定要释放哪个页面）
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 窗口最小化事件
    /// </summary>
    public class WindowMinimizedEvent : PubSubEvent<WindowMinimizedEventArgs>
    {
    }

    /// <summary>
    /// 窗口最小化事件参数
    /// </summary>
    public class WindowMinimizedEventArgs
    {
        /// <summary>
        /// 最小化的窗口
        /// </summary>
        public Window Window { get; set; }

        /// <summary>
        /// 是否保留导航按钮
        /// </summary>
        public bool KeepNavigationButtons { get; set; }
    }

    /// <summary>
    /// 窗口恢复事件（从浮动窗口嵌入回主窗口）
    /// </summary>
    public class WindowRestoredEvent : PubSubEvent<WindowRestoredEventArgs>
    {
    }

    /// <summary>
    /// 窗口恢复事件参数
    /// </summary>
    public class WindowRestoredEventArgs
    {
        /// <summary>
        /// 要恢复的页面名称
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 窗口激活事件（用于窗口焦点感知）
    /// </summary>
    public class WindowActivatedEvent : PubSubEvent<WindowActivatedEventArgs>
    {
    }

    /// <summary>
    /// 窗口激活事件参数
    /// </summary>
    public class WindowActivatedEventArgs
    {
        /// <summary>
        /// 激活的页面名称
        /// </summary>
        public string PageName { get; set; }
    }

    #endregion

    #region Project Events

    /// <summary>
    /// 项目创建事件
    /// </summary>
    public class ProjectCreatedEvent : PubSubEvent<ProjectItem>
    {
    }

    /// <summary>
    /// 项目打开事件
    /// </summary>
    public class ProjectOpenedEvent : PubSubEvent<ProjectItem>
    {
    }

    /// <summary>
    /// 项目保存事件
    /// </summary>
    public class ProjectSavedEvent : PubSubEvent<ProjectItem>
    {
    }

    /// <summary>
    /// 项目开始保存前触发（用于提示页面提交未保存的状态）
    /// </summary>
    public class ProjectSavingEvent : PubSubEvent
    {
    }

    /// <summary>
    /// 项目关闭事件
    /// </summary>
    public class ProjectClosedEvent : PubSubEvent
    {
    }


    /// <summary>
    /// 测试任务创建事件
    /// </summary>
    public class TestTaskCreatedEvent : PubSubEvent<ProjectItem>
    {
    }

    /// <summary>
    /// 测试任务重命名事件
    /// </summary>
    public class TestTaskRenamedEvent : PubSubEvent<RenameTestTaskEventArgs>
    {
    }

    /// <summary>
    /// 测试任务删除事件
    /// </summary>
    public class TestTaskDeletedEvent : PubSubEvent<DeleteTestTaskEventArgs>
    {
    }

    /// <summary>
    /// 重命名测试任务事件参数
    /// </summary>
    public class RenameTestTaskEventArgs
    {
        public ProjectItem TestTask { get; set; }
        public string NewName { get; set; }
    }

    /// <summary>
    /// 删除测试任务事件参数
    /// </summary>
    public class DeleteTestTaskEventArgs
    {
        public ProjectItem TestTask { get; set; }
    }

    #endregion

    #region Navigation Events

    /// <summary>
    /// 导航完成事件
    /// </summary>
    public class NavigationCompletedEvent : PubSubEvent<string>
    {
    }

    #endregion

    #region Application Events

    /// <summary>
    /// 应用关闭事件
    /// </summary>
    public class ApplicationClosingEvent : PubSubEvent
    {
    }

    /// <summary>
    /// 选中项目树节点事件
    /// </summary>
    public class SelectProjectItemEvent : PubSubEvent<SelectProjectItemEventArgs>
    {
    }

    /// <summary>
    /// 选中项目树节点事件参数
    /// </summary>
    public class SelectProjectItemEventArgs
    {
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName { get; set; }

        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTabelName { get; set; }

        /// <summary>
        /// 配置表类型（channel_config_tabel, signal_config_tabel, icd_config_tabel等）
        /// </summary>
        public string ConfigTabelType { get; set; }

        /// <summary>
        /// 是否触发双击导航（默认true）
        /// </summary>
        public bool TriggerDoubleClick { get; set; } = true;
    }

    #endregion

    #region Device Events

    /// <summary>
    /// 设备选择事件
    /// </summary>
    public class DeviceSelectedEvent : PubSubEvent<Models.Devices.DeviceBase>
    {
    }

    #endregion

    #region Connection Events

    /// <summary>
    /// 创建连接事件
    /// </summary>
    public class CreateConnectionEventArgs
    {
        public ChassisConnection Connection { get; set; }
    }

    /// <summary>
    /// 连接创建事件
    /// </summary>
    public class ConnectionCreatedEvent : PubSubEvent<ChassisConnection>
    {
    }

    /// <summary>
    /// 连接删除事件
    /// </summary>
    public class ConnectionDeletedEvent : PubSubEvent<string>
    {
    }

    /// <summary>
    /// 所有连接清除事件
    /// </summary>
    public class AllConnectionsClearedEvent : PubSubEvent
    {
    }

    #endregion

    #region Remote Matrix Command Event

    /// <summary>
    /// 远程矩阵命令参数（从 TCP 收到，转发给对应的 PXI2601_SWITCHViewModel）
    /// </summary>
    public class RemoteMatrixCommandEventArgs
    {
        public int SlotIndex { get; set; }
        public string InputNodeId { get; set; }
        public string OutputNodeId { get; set; }
        public byte State { get; set; }
        public int Port { get; set; }
        public string BoardIdentifier { get; set; }
    }

    /// <summary>
    /// 远程矩阵命令事件
    /// </summary>
    public class RemoteMatrixCommandEvent : PubSubEvent<RemoteMatrixCommandEventArgs>
    {
    }

    #endregion

    #region Device Operation Events

    /// <summary>
    /// 设备添加事件
    /// </summary>
    public class DeviceAddedEvent : PubSubEvent<Models.Devices.DeviceBase>
    {
    }

    /// <summary>
    /// 设备删除事件
    /// </summary>
    public class DeviceDeletedEvent : PubSubEvent<Models.Devices.DeviceBase>
    {
    }

    /// <summary>
    /// 设备展开状态切换事件
    /// </summary>
    public class DeviceExpansionToggledEvent : PubSubEvent<Models.Devices.DeviceBase>
    {
    }

    /// <summary>
    /// 打开机箱连接对话框事件
    /// </summary>
    public class OpenChassisConnectionDialogEvent : PubSubEvent
    {
    }

    /// <summary>
    /// 清除设备详细信息事件
    /// </summary>
    public class ClearDeviceDetailsEvent : PubSubEvent
    {
    }

    #endregion

    #region Channel Config Events

    /// <summary>
    /// 通道配置变化事件（使能状态、量程等）
    /// </summary>
    public class ChannelConfigChangedEvent : PubSubEvent<ChannelConfigChangedEventArgs>
    {
    }

    /// <summary>
    /// 通道配置变化事件参数
    /// </summary>
    public class ChannelConfigChangedEventArgs
    {
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName { get; set; }

        /// <summary>
        /// 板卡ID
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 通道名称
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// 是否使能
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 量程
        /// </summary>
        public string Range { get; set; }
    }

    /// <summary>
    /// 通道使能状态变化事件（用于通知其他视图更新可用通道列表）
    /// </summary>
    public class ChannelEnableChangedEvent : PubSubEvent<ChannelEnableChangedEventArgs>
    {
    }

    /// <summary>
    /// 通道使能变化事件参数
    /// </summary>
    public class ChannelEnableChangedEventArgs
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 板卡名称
        /// </summary>
        public string CardName { get; set; }

        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName { get; set; }
    }

    #endregion

    #region Signal Tabel Events

    /// <summary>
    /// 信号表（变量表）变化事件 - 当添加、删除或修改信号时发布
    /// </summary>
    public class SignalTabelChangedEvent : PubSubEvent<SignalTabelChangedEventArgs>
    {
    }

    /// <summary>
    /// 信号表变化事件参数
    /// </summary>
    public class SignalTabelChangedEventArgs
    {
        /// <summary>变化类型：Added, Removed, Modified</summary>
        public string ChangeType { get; set; }

        /// <summary>信号名称</summary>
        public string SignalName { get; set; }

        /// <summary>实际通道（格式：配置表名:通道名）</summary>
        public string ActualChannel { get; set; }
    }

    #endregion

    #region Test Interface Control Events

    /// <summary>
    /// 控件属性变化事件 - 当控件属性在配置面板中被修改时发布
    /// </summary>
    public class ControlPropertyChangedEvent : PubSubEvent<ControlPropertyChangedEventArgs>
    {
    }

    /// <summary>
    /// 控件属性变化事件参数
    /// </summary>
    public class ControlPropertyChangedEventArgs
    {
        /// <summary>控件ID</summary>
        public string ControlId { get; set; }

        /// <summary>属性名称</summary>
        public string PropertyName { get; set; }

        /// <summary>新值</summary>
        public object NewValue { get; set; }
    }

    #endregion

    #region Test Running Events

    /// <summary>
    /// 测试运行状态变化事件 - 当测试开始/暂停/停止时发布
    /// </summary>
    public class TestRunningStateChangedEvent : PubSubEvent<bool>
    {
    }

    #endregion
}
