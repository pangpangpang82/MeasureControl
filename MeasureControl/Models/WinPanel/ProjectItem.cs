using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;
using System;
using System.Windows;
using System.IO;

namespace MeasureControl.Models
{
    public class ProjectItem : BindableBase
    {
        private string _name;
        private string _icon;
        private string _type;
        private string _communicationChannelName;
        private ObservableCollection<ProjectItem> _children;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Icon
        {
            get => _icon;
            set 
            {
                // 验证图标资源是否存在，如果不存在则使用默认图标
                string validatedIcon = value;
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        // 尝试加载资源以验证其存在性
                        var resourceUri = new Uri(value, UriKind.Relative);
                        var resourceInfo = Application.GetResourceStream(resourceUri);
                        if (resourceInfo == null)
                        {
                            // 资源不存在，使用默认图标
                            validatedIcon = "/Resources/Logo/folder.png"; // 使用文件夹图标作为默认值
                        }
                    }
                    catch (Exception)
                    {
                        // 发生异常，使用默认图标
                        validatedIcon = "/Resources/Logo/folder.png"; // 使用文件夹图标作为默认值
                    }
                }
                SetProperty(ref _icon, validatedIcon); 
            }
        }

        public string Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        public ObservableCollection<ProjectItem> Children
        {
            get => _children ?? (_children = new ObservableCollection<ProjectItem>());
            set => SetProperty(ref _children, value ?? new ObservableCollection<ProjectItem>());
        }

        private string _tag;
        public string Tag
        {
            get => _tag;
            set => SetProperty(ref _tag, value);
        }

        /// <summary>
        /// 文档ID
        /// </summary>
        public string DocumentId { get; set; }

        /// <summary>
        /// ICD配置表的协议类型
        /// </summary>
        public string ProtocolType { get; set; }
        
        /// <summary>
        /// ICD配置表绑定的通讯通道名称，仅ICD配置表节点使用
        /// </summary>
        public string CommunicationChannelName
        {
            get => _communicationChannelName;
            set => SetProperty(ref _communicationChannelName, value);
        }

        // PXI机箱数据存储
        public ObservableCollection<ChassisModel> PxiChassisData { get; set; }

        // 机箱连接数据存储
        public ObservableCollection<ChassisConnection> ChassisConnections { get; set; }

        // 连接线数据存储
        public ObservableCollection<ConnectionLine> ConnectionLines { get; set; }

        // 通道配置表数据存储
        public Dictionary<string, List<ChannelTabelItem>> ChannelTabelItems { get; set; }

        // 信号配置表数据存储
        public Dictionary<string, List<SignalConfigItem>> SignalTabelItems { get; set; }

        // ICD配置表数据存储
        public Dictionary<string, List<IcdFrameItem>> IcdTabelItems { get; set; }

        // 通讯信号配置表数据存储
        public Dictionary<string, List<IcdMappingItem>> IcdMappingItems { get; set; }

        // 测试界面控件数据存储
        public Dictionary<string, List<TestInterfaceControlItem>> TestInterfaceControls { get; set; }

        // 矩阵开关配置表数据存储（key格式：测试任务名/配置表名）
        public Dictionary<string, List<MatrixSwitchConfigItem>> MatrixSwitchTableItems { get; set; }
        // 标定数据存储（校准记录）
        public Dictionary<string, ChannelCalibrationRecord> CalibrationRecords { get; set; }

        public ProjectItem()
        {
            // 直接初始化字段，避免通过属性设置器触发属性更改通知
            _children = new ObservableCollection<ProjectItem>();
            PxiChassisData = new ObservableCollection<ChassisModel>();
            ChassisConnections = new ObservableCollection<ChassisConnection>();
            ConnectionLines = new ObservableCollection<ConnectionLine>();
            ChannelTabelItems = new Dictionary<string, List<ChannelTabelItem>>();
            SignalTabelItems = new Dictionary<string, List<SignalConfigItem>>();
            IcdTabelItems = new Dictionary<string, List<IcdFrameItem>>();
            IcdMappingItems = new Dictionary<string, List<IcdMappingItem>>();
            TestInterfaceControls = new Dictionary<string, List<TestInterfaceControlItem>>();
            MatrixSwitchTableItems = new Dictionary<string, List<MatrixSwitchConfigItem>>();
            CalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>();
        }

        #region 条件序列化 - 只在根节点或数据非空时序列化

        /// <summary>是否为根节点</summary>
        private bool IsRoot => Type == "root";

        public bool ShouldSerializePxiChassisData() => IsRoot || (PxiChassisData != null && PxiChassisData.Count > 0);

        public bool ShouldSerializeChassisConnections() => IsRoot || (ChassisConnections != null && ChassisConnections.Count > 0);

        public bool ShouldSerializeConnectionLines() => IsRoot || (ConnectionLines != null && ConnectionLines.Count > 0);

        public bool ShouldSerializeChannelTabelItems() => IsRoot || (ChannelTabelItems != null && ChannelTabelItems.Count > 0);

        public bool ShouldSerializeSignalTabelItems() => IsRoot || (SignalTabelItems != null && SignalTabelItems.Count > 0);

        public bool ShouldSerializeIcdTabelItems() => IsRoot || (IcdTabelItems != null && IcdTabelItems.Count > 0);

        public bool ShouldSerializeIcdMappingItems() => IsRoot || (IcdMappingItems != null && IcdMappingItems.Count > 0);

        public bool ShouldSerializeTestInterfaceControls() => IsRoot || (TestInterfaceControls != null && TestInterfaceControls.Count > 0);

        public bool ShouldSerializeMatrixSwitchTableItems() => IsRoot || (MatrixSwitchTableItems != null && MatrixSwitchTableItems.Count > 0);
        public bool ShouldSerializeCalibrationRecords() => IsRoot || (CalibrationRecords != null && CalibrationRecords.Count > 0);

        public bool ShouldSerializeDocumentId() => !string.IsNullOrEmpty(DocumentId);

        public bool ShouldSerializeProtocolType() => !string.IsNullOrEmpty(ProtocolType);

        public bool ShouldSerializeCommunicationChannelName() => !string.IsNullOrEmpty(CommunicationChannelName);

        #endregion
    }
}
