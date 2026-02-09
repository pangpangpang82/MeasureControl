using System;
using System.Configuration;
using System.Diagnostics;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 应用程序常量配置
    /// </summary>
    public static class AppConstants
    {
        private static bool _localChassisInitialized;
        private static string _localChassisName;
        private static string _localChassisNameSource;
        private static bool? _enableLocalChassisDebug;

        private static bool _arinc429RealProductInitialized;
        private static bool _arinc429IsRealProduct;
        private static string _arinc429RealProductSource;

        private static bool EnableLocalChassisDebug
        {
            get
            {
                if (_enableLocalChassisDebug.HasValue) return _enableLocalChassisDebug.Value;

                bool enabled = false;
                try
                {
                    var raw = ConfigurationManager.AppSettings["LocalChassisDebug"];
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        raw = raw.Trim();
                        enabled = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    enabled = false;
                }

#if DEBUG
                enabled = enabled || true;
#endif

                _enableLocalChassisDebug = enabled;
                return enabled;
            }
        }

        public static string LocalChassisName
        {
            get
            {
                EnsureLocalChassisInitialized();
                return _localChassisName;
            }
        }

        public static bool Arinc429IsRealProduct
        {
            get
            {
                EnsureArinc429RealProductInitialized();
                return _arinc429IsRealProduct;
            }
        }

        public static bool IsLocalChassis(string chassisName)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
            {
                if (EnableLocalChassisDebug)
                {
                    Debug.WriteLine("[LocalChassis] IsLocalChassis: chassisName is null/empty -> false");
                }
                return false;
            }

            EnsureLocalChassisInitialized();

            if (string.IsNullOrWhiteSpace(_localChassisName))
            {
                if (EnableLocalChassisDebug)
                {
                    Debug.WriteLine($"[LocalChassis] IsLocalChassis: chassisName='{chassisName}' localChassisName is null/empty (source={_localChassisNameSource ?? "none"}) -> false");
                }
                return false;
            }

            var left = chassisName.Trim();
            var right = _localChassisName.Trim();
            var result = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

            if (EnableLocalChassisDebug)
            {
                Debug.WriteLine($"[LocalChassis] IsLocalChassis: input='{chassisName}'(trim='{left}') local='{_localChassisName}'(trim='{right}') source={_localChassisNameSource ?? "none"} -> {result}");
            }

            return result;
        }

        private static void EnsureLocalChassisInitialized()
        {
            if (_localChassisInitialized) return;
            _localChassisInitialized = true;

            if (EnableLocalChassisDebug)
            {
                try
                {
                    Debug.WriteLine($"[LocalChassis] ConfigFile: '{AppDomain.CurrentDomain.SetupInformation.ConfigurationFile}'");
                    var args = Environment.GetCommandLineArgs();
                    Debug.WriteLine($"[LocalChassis] CommandLineArgs: {(args == null ? "<null>" : string.Join(" ", args))}");
                }
                catch
                {
                }
            }

            var fromArgs = TryGetLocalChassisNameFromArgs();
            if (!string.IsNullOrWhiteSpace(fromArgs))
            {
                _localChassisName = fromArgs;
                _localChassisNameSource = "args";
                if (EnableLocalChassisDebug)
                {
                    Debug.WriteLine($"[LocalChassis] Initialized from args: '{_localChassisName}'");
                }
                return;
            }

            var fromConfig = TryGetLocalChassisNameFromAppConfig();
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                _localChassisName = fromConfig;
                _localChassisNameSource = "app.config";
                if (EnableLocalChassisDebug)
                {
                    Debug.WriteLine($"[LocalChassis] Initialized from app.config: '{_localChassisName}'");
                }
                return;
            }

            _localChassisName = null;
            _localChassisNameSource = "none";
            if (EnableLocalChassisDebug)
            {
                Debug.WriteLine("[LocalChassis] Initialized: LocalChassisName not found in args or app.config");
            }
        }

        private static void EnsureArinc429RealProductInitialized()
        {
            if (_arinc429RealProductInitialized) return;
            _arinc429RealProductInitialized = true;

            bool enabled = false;
            string source = "none";

            var fromArgs = TryGetBoolFromArgs("--arinc429RealProduct=", "-arinc429RealProduct=", "/arinc429RealProduct:");
            if (fromArgs.HasValue)
            {
                enabled = fromArgs.Value;
                source = "args";
            }
            else
            {
                try
                {
                    var raw = ConfigurationManager.AppSettings["Arinc429RealProduct"];
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        raw = raw.Trim();
                        enabled = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        source = "app.config";
                    }
                }
                catch
                {
                    enabled = false;
                    source = "error";
                }
            }

            _arinc429IsRealProduct = enabled;
            _arinc429RealProductSource = source;
        }

        private static bool? TryGetBoolFromArgs(string key1, string key2, string key3)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args == null) return null;

                foreach (var raw in args)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var arg = raw.Trim();

                    if (!string.IsNullOrWhiteSpace(key1) && arg.StartsWith(key1, StringComparison.OrdinalIgnoreCase))
                        return ParseBool(arg.Substring(key1.Length));
                    if (!string.IsNullOrWhiteSpace(key2) && arg.StartsWith(key2, StringComparison.OrdinalIgnoreCase))
                        return ParseBool(arg.Substring(key2.Length));
                    if (!string.IsNullOrWhiteSpace(key3) && arg.StartsWith(key3, StringComparison.OrdinalIgnoreCase))
                        return ParseBool(arg.Substring(key3.Length));
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static bool ParseBool(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim();
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryGetLocalChassisNameFromArgs()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args == null) return null;

                foreach (var raw in args)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var arg = raw.Trim();

                    const string key1 = "--localChassis=";
                    const string key2 = "-localChassis=";
                    const string key3 = "/localChassis:";

                    if (arg.StartsWith(key1, StringComparison.OrdinalIgnoreCase))
                        return arg.Substring(key1.Length).Trim();
                    if (arg.StartsWith(key2, StringComparison.OrdinalIgnoreCase))
                        return arg.Substring(key2.Length).Trim();
                    if (arg.StartsWith(key3, StringComparison.OrdinalIgnoreCase))
                        return arg.Substring(key3.Length).Trim();
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string TryGetLocalChassisNameFromAppConfig()
        {
            try
            {
                return ConfigurationManager.AppSettings["LocalChassisName"]?.Trim();
            }
            catch
            {
                return null;
            }
        }

        #region 文件和路径常量

        /// <summary>
        /// 项目文件扩展名
        /// </summary>
        public const string ProjectFileExtension = ".json";

        /// <summary>
        /// 项目文件过滤器
        /// </summary>
        public const string ProjectFileFilter = "项目文件 (*.json)|*.json";

        /// <summary>
        /// 默认项目名称
        /// </summary>
        public const string DefaultProjectName = "新项目";

        #endregion

        #region 机箱配置常量

        /// <summary>
        /// 默认机箱名称前缀
        /// </summary>
        public const string DefaultChassisNamePrefix = "PXI机箱";

        /// <summary>
        /// 每行最大机箱数量
        /// </summary>
        public const int MaxChassisPerRow = 4;

        /// <summary>
        /// 最大行数
        /// </summary>
        public const int MaxChassisRows = 4;

        /// <summary>
        /// 9槽机箱的槽位数量
        /// </summary>
        public const int NineSlotChassisSlotCount = 9;

        #endregion

        #region 设备配置常量

        /// <summary>
        /// 机箱设备类型
        /// </summary>
        public const string DeviceTypeChassis = "Chassis";

        /// <summary>
        /// 板卡设备类型
        /// </summary>
        public const string DeviceTypeCard = "Card";

        /// <summary>
        /// 仪器设备类型
        /// </summary>
        public const string DeviceTypeInstrument = "Instrument";

        /// <summary>
        /// 设备默认状态
        /// </summary>
        public const string DeviceDefaultStatus = "正常";

        /// <summary>
        /// 默认插槽位置
        /// </summary>
        public const string DefaultSlotPosition = "N/A";

        #endregion

        #region UI配置常量

        /// <summary>
        /// 主区域名称
        /// </summary>
        public const string MainRegionName = "MainRegion";

        /// <summary>
        /// 首页视图名称
        /// </summary>
        public const string HomePageViewName = "HomePage";

        /// <summary>
        /// 硬件配置视图名称
        /// </summary>
        public const string HardwareConfigViewName = "HardwareConfig";

        /// <summary>
        /// PXI机箱视图名称
        /// </summary>
        public const string PxiChassisViewName = "PxiChassis";

        /// <summary>
        /// 时间格式
        /// </summary>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 设备信息默认标题
        /// </summary>
        public const string DeviceInfoDefaultTitle = "暂无信息";

        /// <summary>
        /// 设备详细信息标题
        /// </summary>
        public const string DeviceDetailsTitle = "设备详细信息";

        #endregion

        #region 项目树节点类型

        /// <summary>
        /// 根节点类型
        /// </summary>
        public const string NodeTypeRoot = "root";

        /// <summary>
        /// 硬件配置节点类型
        /// </summary>
        public const string NodeTypeHardwareConfig = "Hardware_config";

        /// <summary>
        /// 设备节点类型
        /// </summary>
        public const string NodeTypeDevice = "device";

        /// <summary>
        /// 任务配置节点类型
        /// </summary>
        public const string NodeTypeTaskConfig = "task_config";

        /// <summary>
        /// 测试任务节点类型
        /// </summary>
        public const string NodeTypeTestTask = "test_task";

        /// <summary>
        /// 数据分析节点类型
        /// </summary>
        public const string NodeTypeDataAnalysis = "data_analysis";

        /// <summary>
        /// 数据库管理节点类型
        /// </summary>
        public const string NodeTypeDatabaseManagement = "database_management";

        /// <summary>
        /// 远程接口节点类型
        /// </summary>
        public const string NodeTypeRemoteInterface = "remote_interface";

        /// <summary>
        /// PXI机箱节点类型
        /// </summary>
        public const string NodeTypePxiChassis = "PXIChassis";

        #endregion

        #region 项目树节点名称

        /// <summary>
        /// 硬件配置节点名称
        /// </summary>
        public const string NodeNameHardwareConfig = "硬件配置";

        /// <summary>
        /// 设备与网络节点名称
        /// </summary>
        public const string NodeNameDeviceNetwork = "设备与网络";

        /// <summary>
        /// 任务配置节点名称
        /// </summary>
        public const string NodeNameTaskConfig = "任务配置";

        /// <summary>
        /// 数据分析节点名称
        /// </summary>
        public const string NodeNameDataAnalysis = "数据分析";

        /// <summary>
        /// 数据库管理节点名称
        /// </summary>
        public const string NodeNameDatabaseManagement = "数据库管理";

        /// <summary>
        /// 远程接口节点名称
        /// </summary>
        public const string NodeNameRemoteInterface = "远程接口";

        /// <summary>
        /// 测试任务名称前缀
        /// </summary>
        public const string TestTaskNamePrefix = "测试任务";

        #endregion

        #region 连接配置常量

        /// <summary>
        /// 以太网连接显示名称
        /// </summary>
        public const string ConnectionTypeEthernetDisplay = "以太网连接";

        /// <summary>
        /// USB连接显示名称
        /// </summary>
        public const string ConnectionTypeUsbDisplay = "USB连接";

        /// <summary>
        /// 串口连接显示名称
        /// </summary>
        public const string ConnectionTypeSerialDisplay = "串口连接";

        /// <summary>
        /// 以太网默认速率
        /// </summary>
        public const string EthernetDefaultSpeed = "1000 Mbps";

        /// <summary>
        /// USB默认速率
        /// </summary>
        public const string UsbDefaultSpeed = "480 Mbps";

        /// <summary>
        /// 串口默认速率
        /// </summary>
        public const string SerialDefaultSpeed = "115200 bps";

        /// <summary>
        /// 以太网默认协议
        /// </summary>
        public const string EthernetDefaultProtocol = "TCP/IP";

        /// <summary>
        /// USB默认协议
        /// </summary>
        public const string UsbDefaultProtocol = "USB 2.0";

        /// <summary>
        /// 串口默认协议
        /// </summary>
        public const string SerialDefaultProtocol = "RS-232";

        #endregion

        #region 消息文本常量

        /// <summary>
        /// 项目保存成功消息
        /// </summary>
        public const string MessageProjectSaveSuccess = "项目保存成功！";

        /// <summary>
        /// 项目保存失败消息前缀
        /// </summary>
        public const string MessageProjectSaveFailedPrefix = "保存项目失败：";

        /// <summary>
        /// 项目加载失败消息前缀
        /// </summary>
        public const string MessageProjectLoadFailedPrefix = "加载项目失败：";

        /// <summary>
        /// 确认删除机箱消息前缀
        /// </summary>
        public const string MessageConfirmDeleteChassisPrefix = "确定要删除机箱";

        /// <summary>
        /// 机箱已满消息
        /// </summary>
        public const string MessageChassisFull = "槽机箱最多只能添加{0}个板卡设备！";

        /// <summary>
        /// 请先添加机箱设备
        /// </summary>
        public const string MessageAddChassisFirst = "请先添加机箱设备";

        /// <summary>
        /// 一个PXI机箱只能添加一个机箱设备
        /// </summary>
        public const string MessageOnlyOneChassisDevice = "一个PXI机箱只能添加一个机箱设备";

        #endregion

        #region 图标路径常量

        /// <summary>
        /// 文件夹图标路径
        /// </summary>
        public const string IconFolder = "/Resources/Logo/folder.png";

        /// <summary>
        /// 硬件图标路径
        /// </summary>
        public const string IconHardware = "/Resources/Logo/hardware_p.png";

        /// <summary>
        /// 任务图标路径
        /// </summary>
        public const string IconTasks = "/Resources/Logo/tasks.png";

        /// <summary>
        /// 监控图标路径
        /// </summary>
        public const string IconMonitor = "/Resources/Logo/monitor.png";

        /// <summary>
        /// 数据库图标路径
        /// </summary>
        public const string IconDatabase = "/Resources/Logo/database.png";

        /// <summary>
        /// 手势图标路径
        /// </summary>
        public const string IconHand = "/Resources/Logo/hand.png";

        /// <summary>
        /// 信号图标路径
        /// </summary>
        public const string IconSignal = "/Resources/Logo/signal.png";

        /// <summary>
        /// 非通讯变量图标路径
        /// </summary>
        public const string IconCommunicate = "/Resources/Logo/ncommunicate.png";

        /// <summary>
        /// 通讯变量图标路径
        /// </summary>
        public const string IconNonCommunicate = "/Resources/Logo/communicate.png";

        /// <summary>
        /// 测试图标路径
        /// </summary>
        public const string IconTest = "/Resources/Logo/test.png";

        /// <summary>
        /// 测试脚本图标路径
        /// </summary>
        public const string IconTestScript = "/Resources/Logo/test_script.png";

        /// <summary>
        /// 红色文件图标路径
        /// </summary>
        public const string IconFileRed = "/Resources/Logo/file_red.png";

        /// <summary>
        /// 电源图标路径
        /// </summary>
        public const string IconPower = "/Resources/Logo/power.png";

        /// <summary>
        /// 仪器图标路径
        /// </summary>
        public const string IconInstrument = "/Resources/Logo/instrument.png";

        /// <summary>
        /// 芯片图标路径
        /// </summary>
        public const string IconChip = "/Resources/Logo/chip_b.png";

        /// <summary>
        /// 表格图标路径
        /// </summary>
        public const string IconTabel = "/Resources/Logo/tabel_b.png";

        /// <summary>
        /// ICD 映射节点图标路径
        /// </summary>
        public const string IconMapping = "/Resources/Logo/mapping.png";

        #endregion
    }
}
