using System;
using System.Collections.Generic;
using System.Linq;

namespace MeasureControl.Services
{
    /// <summary>
    /// 页面定义 - 描述页面的元数据信息
    /// </summary>
    public class PageDefinition
    {
        /// <summary>
        /// 页面类型标识（如 "HardwareConfig"）
        /// </summary>
        public string PageType { get; set; }

        /// <summary>
        /// Prism 视图名称（用于导航）
        /// </summary>
        public string ViewName { get; set; }

        /// <summary>
        /// 显示名称（中文名称，如"硬件配置"）
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 导航按钮名称（如"设备与网络"）
        /// </summary>
        public string ButtonName { get; set; }

        /// <summary>
        /// 是否单例页面（true表示全局只能有一个实例，false表示可以有多个实例）
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// 优先级（数字越小优先级越高，用于导航按钮排序）
        /// 0: 设备与网络
        /// 1: PXI机箱
        /// 2: 测试任务相关页面
        /// 3: 其他页面
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 页面类别（用于分组和查询）
        /// </summary>
        public PageCategory Category { get; set; }
    }

    /// <summary>
    /// 页面类别枚举
    /// </summary>
    public enum PageCategory
    {
        /// <summary>
        /// 首页
        /// </summary>
        Home,

        /// <summary>
        /// 硬件配置相关
        /// </summary>
        Hardware,

        /// <summary>
        /// 数据库管理相关
        /// </summary>
        Database,

        /// <summary>
        /// 测试任务相关
        /// </summary>
        TestTask,

        /// <summary>
        /// 系统配置
        /// </summary>
        System
    }

    /// <summary>
    /// 导航注册表 - 集中管理所有页面的元数据和映射关系
    /// 参考博图软件的导航模式，实现页面配置的统一管理
    /// </summary>
    public class NavigationRegistry
    {
        private readonly Dictionary<string, PageDefinition> _pageDefinitions;

        public NavigationRegistry()
        {
            _pageDefinitions = new Dictionary<string, PageDefinition>();
            RegisterAllPages();
        }

        /// <summary>
        /// 注册所有页面配置
        /// </summary>
        private void RegisterAllPages()
        {
            // 单例页面 - 全局只能有一个实例

            // 首页（HomePage）- 最低优先级，作为默认后备页面
            Register(new PageDefinition
            {
                PageType = "HomePage",
                ViewName = "HomePage",
                DisplayName = "首页",
                ButtonName = "首页",
                IsSingleton = true,
                Priority = 999,
                Category = PageCategory.Home
            });

            // 设备与网络（硬件配置）
            Register(new PageDefinition
            {
                PageType = "HardwareConfig",
                ViewName = "HardwareConfig",
                DisplayName = "硬件配置",
                ButtonName = "设备与网络",
                IsSingleton = true,
                Priority = 0,
                Category = PageCategory.Hardware
            });

            // TDM系统
            Register(new PageDefinition
            {
                PageType = "TDMSystem",
                ViewName = "TDMSystem",
                DisplayName = "TDM系统",
                ButtonName = "TDM系统",
                IsSingleton = true,
                Priority = 3,
                Category = PageCategory.System
            });

            // 数据库管理
            Register(new PageDefinition
            {
                PageType = "DatabaseConfig",
                ViewName = "DatabaseConfig",
                DisplayName = "数据库管理",
                ButtonName = "数据库管理",
                IsSingleton = true,
                Priority = 3,
                Category = PageCategory.Database
            });

            // 多例页面 - 可以同时打开多个实例

            // PXI机箱（每个机箱独立实例）
            Register(new PageDefinition
            {
                PageType = "PxiChassis",
                ViewName = "PxiChassis",
                DisplayName = "PXI机箱",
                ButtonName = "PXI机箱",
                IsSingleton = false,
                Priority = 1,
                Category = PageCategory.Hardware
            });

            // 通道配置表（每个配置表独立实例）
            Register(new PageDefinition
            {
                PageType = "ChannelConfigTabel",
                ViewName = "ChannelConfigTabel",
                DisplayName = "通道配置表",
                ButtonName = "通道配置表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 信号配置表（每个配置表独立实例）
            Register(new PageDefinition
            {
                PageType = "SignalConfigTabel",
                ViewName = "SignalConfigTabel",
                DisplayName = "信号配置表",
                ButtonName = "信号配置表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 矩阵开关配置表（每个配置表独立实例）
            Register(new PageDefinition
            {
                PageType = "MatrixSwitchConfigTable",
                ViewName = "MatrixSwitchConfigTable",
                DisplayName = "矩阵开关配置表",
                ButtonName = "矩阵开关配置表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 通讯变量表
            Register(new PageDefinition
            {
                PageType = "CommunicatingSignalConfigTabel",
                ViewName = "CommunicatingSignalConfigTabel",
                DisplayName = "通讯变量表",
                ButtonName = "通讯变量表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // ICD映射表
            Register(new PageDefinition
            {
                PageType = "IcdMappingTabel",
                ViewName = "IcdMappingTabel",
                DisplayName = "ICD映射表",
                ButtonName = "ICD映射表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // ICD配置表（每个配置表独立实例）
            Register(new PageDefinition
            {
                PageType = "IcdConfigTabel",
                ViewName = "IcdConfigTabel",
                DisplayName = "ICD配置表",
                ButtonName = "ICD配置表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 测试序列（每个测试序列独立实例）
            Register(new PageDefinition
            {
                PageType = "TestSequence",
                ViewName = "TestSequence",
                DisplayName = "测试序列",
                ButtonName = "测试序列",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 报表配置表（每个报表配置独立实例）
            Register(new PageDefinition
            {
                PageType = "ReportConfigTabel",
                ViewName = "ReportConfigTabel",
                DisplayName = "报表配置表",
                ButtonName = "报表配置表",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 测试界面（每个测试界面独立实例）
            Register(new PageDefinition
            {
                PageType = "TestInterface",
                ViewName = "TestInterface",
                DisplayName = "测试界面",
                ButtonName = "测试界面",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });

            // 单板测试（每个单板测试独立实例）
            Register(new PageDefinition
            {
                PageType = "BoardTest",
                ViewName = "BoardTest",
                DisplayName = "单板测试",
                ButtonName = "单板测试",
                IsSingleton = false,
                Priority = 2,
                Category = PageCategory.TestTask
            });
        }

        /// <summary>
        /// 注册页面定义
        /// </summary>
        private void Register(PageDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (string.IsNullOrEmpty(definition.PageType))
                throw new ArgumentException("PageType cannot be null or empty");

            _pageDefinitions[definition.PageType] = definition;
        }

        /// <summary>
        /// 获取页面定义
        /// </summary>
        public PageDefinition GetPageDefinition(string pageType)
        {
            return _pageDefinitions.TryGetValue(pageType, out var definition) ? definition : null;
        }

        /// <summary>
        /// 根据按钮名称获取页面定义
        /// </summary>
        public PageDefinition GetPageDefinitionByButtonName(string buttonName)
        {
            return _pageDefinitions.Values.FirstOrDefault(p => p.ButtonName == buttonName);
        }

        /// <summary>
        /// 获取视图名称
        /// </summary>
        public string GetViewName(string pageType)
        {
            return GetPageDefinition(pageType)?.ViewName;
        }

        /// <summary>
        /// 获取显示名称
        /// </summary>
        public string GetDisplayName(string pageType)
        {
            return GetPageDefinition(pageType)?.DisplayName ?? pageType;
        }

        /// <summary>
        /// 获取按钮名称
        /// </summary>
        public string GetButtonName(string pageType)
        {
            return GetPageDefinition(pageType)?.ButtonName ?? pageType;
        }

        /// <summary>
        /// 判断是否为单例页面
        /// </summary>
        public bool IsSingleton(string pageType)
        {
            return GetPageDefinition(pageType)?.IsSingleton ?? false;
        }

        /// <summary>
        /// 获取页面优先级
        /// </summary>
        public int GetPriority(string pageType)
        {
            return GetPageDefinition(pageType)?.Priority ?? int.MaxValue;
        }

        /// <summary>
        /// 判断页面类型是否已注册
        /// </summary>
        public bool IsRegistered(string pageType)
        {
            return _pageDefinitions.ContainsKey(pageType);
        }

        /// <summary>
        /// 获取所有已注册的页面类型
        /// </summary>
        public IEnumerable<string> GetAllPageTypes()
        {
            return _pageDefinitions.Keys;
        }

        /// <summary>
        /// 根据类别获取页面定义列表
        /// </summary>
        public IEnumerable<PageDefinition> GetPagesByCategory(PageCategory category)
        {
            return _pageDefinitions.Values.Where(p => p.Category == category);
        }
    }
}

