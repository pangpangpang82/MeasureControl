using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Models.Devices.Configurators;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// DMM测量类型枚举
    /// </summary>
    public enum DmmMeasurementType
    {
        DCV,        // DC电压
        ACV,        // AC电压
        DCI,        // DC电流
        ACI,        // AC电流
        RES_2W,     // 2线电阻
        RES_4W,     // 4线电阻
        CAP,        // 电容
        DIODE,      // 二极管
        CONT,       // 通断
        FREQ,       // 频率
        PER,        // 周期
        TEMP_TC,    // 温度-热电偶
        TEMP_RTD,   // 温度-RTD
        TEMP_THER,  // 温度-热敏电阻
        SENSOR      // 任意传感器
    }

    /// <summary>
    /// 温度传感器类型枚举
    /// </summary>
    public enum TemperatureSensorType
    {
        // 热电偶 (TC)
        TC_B,           // B型热电偶
        TC_E,           // E型热电偶
        TC_J,           // J型热电偶
        TC_K,           // K型热电偶
        TC_N,           // N型热电偶
        TC_R,           // R型热电偶
        TC_S,           // S型热电偶
        TC_T,           // T型热电偶

        // 电阻温度检测器 (RTD)
        RTD_Pt100,      // Pt100 (α=0.00385)
        RTD_Pt385,      // Pt100 (α=0.003916)
        RTD_Cu10,       // Cu10
        RTD_Cu50,       // Cu50

        // 热敏电阻 (Thermistor)
        THER_2252,      // 2252Ω热敏电阻
        THER_5000,      // 5000Ω热敏电阻
        THER_10000      // 10000Ω热敏电阻
    }

    /// <summary>
    /// 触发模式枚举
    /// </summary>
    public enum DmmTriggerMode
    {
        Auto,       // 自动触发
        Manual,     // 手动触发
        Bus,        // 总线触发
        External    // 外部触发
    }

    /// <summary>
    /// 数字万用表设备类
    /// </summary>
    public class DmmDevice : InstrumentDeviceBase
    {
        // 基本参数
        private double _resolution;
        private int _displayCount;
        private int _samplingRate;
        private int _storageCapacity;

        // 测量功能
        private string _supportedMeasurements;
        private DmmMeasurementType _currentMeasurementMode;
        private bool _trueRmsSupport;

        // 精度参数
        private string _dcvAccuracy;
        private string _acvAccuracy;
        private string _dciAccuracy;
        private string _aciAccuracy;
        private string _resAccuracy;
        private string _capAccuracy;
        private string _freqAccuracy;
        private string _tempAccuracy;

        // 温度测量
        private string _supportedTempSensors;
        private bool _internalColdJunction;

        // 数学功能
        private string _mathFunctions;

        // 触发功能
        private DmmTriggerMode _triggerMode;
        private string _integrationTime;

        // 通信接口
        private bool _interfaceUSB;
        private bool _interfaceLAN;
        private bool _interfaceRS232;
        private bool _interfaceGPIB;
        private string _protocolSupport;

        // 显示功能
        private bool _dualDisplay;
        private bool _trendChart;
        private bool _histogram;

        // 其他功能
        private bool _arbitrarySensorSupport;
        private bool _cloneFunction;
        private string _pcSoftware;

        #region 基本参数属性
        /// <summary>
        /// 分辨率 (位数)
        /// </summary>
        public double Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        /// <summary>
        /// 显示Count数
        /// </summary>
        public int DisplayCount
        {
            get => _displayCount;
            set => SetProperty(ref _displayCount, value);
        }

        /// <summary>
        /// 采样速率 (rdgs/s)
        /// </summary>
        public int SamplingRate
        {
            get => _samplingRate;
            set => SetProperty(ref _samplingRate, value);
        }

        /// <summary>
        /// 存储容量 (rdgs)
        /// </summary>
        public int StorageCapacity
        {
            get => _storageCapacity;
            set => SetProperty(ref _storageCapacity, value);
        }
        #endregion

        #region 测量功能属性
        /// <summary>
        /// 支持的测量类型
        /// </summary>
        public string SupportedMeasurements
        {
            get => _supportedMeasurements;
            set => SetProperty(ref _supportedMeasurements, value);
        }

        /// <summary>
        /// 当前测量模式
        /// </summary>
        public DmmMeasurementType CurrentMeasurementMode
        {
            get => _currentMeasurementMode;
            set => SetProperty(ref _currentMeasurementMode, value);
        }

        /// <summary>
        /// 支持真RMS
        /// </summary>
        public bool TrueRmsSupport
        {
            get => _trueRmsSupport;
            set => SetProperty(ref _trueRmsSupport, value);
        }
        #endregion

        #region 精度参数属性
        /// <summary>
        /// DC电压精度
        /// </summary>
        public string DcvAccuracy
        {
            get => _dcvAccuracy;
            set => SetProperty(ref _dcvAccuracy, value);
        }

        /// <summary>
        /// AC电压精度
        /// </summary>
        public string AcvAccuracy
        {
            get => _acvAccuracy;
            set => SetProperty(ref _acvAccuracy, value);
        }

        /// <summary>
        /// DC电流精度
        /// </summary>
        public string DciAccuracy
        {
            get => _dciAccuracy;
            set => SetProperty(ref _dciAccuracy, value);
        }

        /// <summary>
        /// AC电流精度
        /// </summary>
        public string AciAccuracy
        {
            get => _aciAccuracy;
            set => SetProperty(ref _aciAccuracy, value);
        }

        /// <summary>
        /// 电阻精度
        /// </summary>
        public string ResAccuracy
        {
            get => _resAccuracy;
            set => SetProperty(ref _resAccuracy, value);
        }

        /// <summary>
        /// 电容精度
        /// </summary>
        public string CapAccuracy
        {
            get => _capAccuracy;
            set => SetProperty(ref _capAccuracy, value);
        }

        /// <summary>
        /// 频率精度
        /// </summary>
        public string FreqAccuracy
        {
            get => _freqAccuracy;
            set => SetProperty(ref _freqAccuracy, value);
        }

        /// <summary>
        /// 温度精度
        /// </summary>
        public string TempAccuracy
        {
            get => _tempAccuracy;
            set => SetProperty(ref _tempAccuracy, value);
        }
        #endregion

        #region 温度测量属性
        /// <summary>
        /// 支持的温度传感器
        /// </summary>
        public string SupportedTempSensors
        {
            get => _supportedTempSensors;
            set => SetProperty(ref _supportedTempSensors, value);
        }

        /// <summary>
        /// 内置冷端补偿
        /// </summary>
        public bool InternalColdJunction
        {
            get => _internalColdJunction;
            set => SetProperty(ref _internalColdJunction, value);
        }
        #endregion

        #region 数学功能属性
        /// <summary>
        /// 数学功能列表
        /// </summary>
        public string MathFunctions
        {
            get => _mathFunctions;
            set => SetProperty(ref _mathFunctions, value);
        }
        #endregion

        #region 触发功能属性
        /// <summary>
        /// 触发模式
        /// </summary>
        public DmmTriggerMode TriggerMode
        {
            get => _triggerMode;
            set => SetProperty(ref _triggerMode, value);
        }

        /// <summary>
        /// 积分时间
        /// </summary>
        public string IntegrationTime
        {
            get => _integrationTime;
            set => SetProperty(ref _integrationTime, value);
        }
        #endregion

        #region 通信接口属性
        /// <summary>
        /// USB接口
        /// </summary>
        public bool InterfaceUSB
        {
            get => _interfaceUSB;
            set => SetProperty(ref _interfaceUSB, value);
        }

        /// <summary>
        /// LAN接口
        /// </summary>
        public bool InterfaceLAN
        {
            get => _interfaceLAN;
            set => SetProperty(ref _interfaceLAN, value);
        }

        /// <summary>
        /// RS232接口
        /// </summary>
        public bool InterfaceRS232
        {
            get => _interfaceRS232;
            set => SetProperty(ref _interfaceRS232, value);
        }

        /// <summary>
        /// GPIB接口
        /// </summary>
        public bool InterfaceGPIB
        {
            get => _interfaceGPIB;
            set => SetProperty(ref _interfaceGPIB, value);
        }

        /// <summary>
        /// 协议支持
        /// </summary>
        public string ProtocolSupport
        {
            get => _protocolSupport;
            set => SetProperty(ref _protocolSupport, value);
        }
        #endregion

        #region 显示功能属性
        /// <summary>
        /// 双显示支持
        /// </summary>
        public bool DualDisplay
        {
            get => _dualDisplay;
            set => SetProperty(ref _dualDisplay, value);
        }

        /// <summary>
        /// 趋势图
        /// </summary>
        public bool TrendChart
        {
            get => _trendChart;
            set => SetProperty(ref _trendChart, value);
        }

        /// <summary>
        /// 直方图
        /// </summary>
        public bool Histogram
        {
            get => _histogram;
            set => SetProperty(ref _histogram, value);
        }
        #endregion

        #region 其他功能属性
        /// <summary>
        /// 任意传感器支持
        /// </summary>
        public bool ArbitrarySensorSupport
        {
            get => _arbitrarySensorSupport;
            set => SetProperty(ref _arbitrarySensorSupport, value);
        }

        /// <summary>
        /// 克隆功能
        /// </summary>
        public bool CloneFunction
        {
            get => _cloneFunction;
            set => SetProperty(ref _cloneFunction, value);
        }

        /// <summary>
        /// PC配套软件
        /// </summary>
        public string PcSoftware
        {
            get => _pcSoftware;
            set => SetProperty(ref _pcSoftware, value);
        }
        #endregion

        public override string DeviceTypeName => "数字万用表";

        public DmmDevice() : base()
        {
            DeviceType = Constants.DeviceConstants.Type.Instrument;
            ParentNode = "数字万用表";

            // 使用配置器配置默认型号 DM3068
            DeviceConfiguratorFactory.TryConfigure(this, "DM3068");
            InitializeChildren();
        }

        public DmmDevice(string name, string slotPosition) : base()
        {
            DeviceType = Constants.DeviceConstants.Type.Instrument;
            ParentNode = "数字万用表";
            Model = "DM3068";
            ParseDeviceName(name); // 使用基类方法
            SlotPosition = slotPosition;

            // 根据型号配置设备
            ConfigureByModel(name);

            InitializeChildren();
        }

        /// <summary>
        /// 根据型号配置设备参数
        /// </summary>
        private void ConfigureByModel(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                // 使用默认DM3068配置
                deviceName = "DM3068";
            }

            // 尝试使用配置器工厂自动配置
            bool configured = DeviceConfiguratorFactory.TryConfigure(this, deviceName);

            // 如果没有找到专用配置器，使用默认DM3068配置
            if (!configured)
            {
                DeviceConfiguratorFactory.TryConfigure(this, "DM3068");
            }

        }

        /// <summary>
        /// 添加DMM测量功能规格
        /// </summary>
        private void AddDmmMeasurementSpecs(DeviceSpecificationBuilder builder)
        {
            // DC电压
            builder.AddGroup("测量功能-DC电压")
                .Add("量程", "200mV/2V/20V/200V/1000V")
                .Add("精度", DcvAccuracy)
                .Add("输入阻抗", ">10GΩ (<200V), 10MΩ (1000V)")
                .Add("最大输入", "1000V");

            // AC电压
            builder.AddGroup("测量功能-AC电压")
                .Add("量程", "200mV/2V/20V/200V/750V")
                .Add("精度", AcvAccuracy)
                .Add("频率范围", "20Hz ~ 100kHz")
                .Add("真RMS", TrueRmsSupport ? "支持" : "不支持")
                .Add("最大输入", "750Vrms");

            // DC电流
            builder.AddGroup("测量功能-DC电流")
                .Add("量程", "200μA/2mA/20mA/200mA/10A")
                .Add("精度", DciAccuracy)
                .Add("最大输入", "10A (连续)");

            // AC电流
            builder.AddGroup("测量功能-AC电流")
                .Add("量程", "20mA/200mA/10A")
                .Add("精度", AciAccuracy)
                .Add("频率范围", "20Hz ~ 10kHz")
                .Add("真RMS", TrueRmsSupport ? "支持" : "不支持")
                .Add("最大输入", "10Arms (连续)");

            // 电阻
            builder.AddGroup("测量功能-电阻")
                .Add("量程", "200Ω/2kΩ/20kΩ/200kΩ/2MΩ/20MΩ/100MΩ")
                .Add("精度 (2线)", "0.02% + 0.002%")
                .Add("精度 (4线)", ResAccuracy)
                .Add("测量电流", "1μA ~ 1mA (自动)")
                .Add("开路电压", "<10V");

            // 电容
            builder.AddGroup("测量功能-电容")
                .Add("量程", "2nF/20nF/200nF/2μF/20μF/200μF")
                .Add("精度", CapAccuracy)
                .Add("最大输入", "200μF");

            // 温度
            if (!string.IsNullOrEmpty(SupportedTempSensors))
            {
                builder.AddGroup("测量功能-温度")
                    .Add("传感器类型", SupportedTempSensors)
                    .Add("热电偶范围", "-200°C ~ +1820°C (取决于类型)")
                    .Add("RTD范围", "-200°C ~ +800°C")
                    .Add("热敏电阻范围", "-80°C ~ +150°C")
                    .Add("精度", TempAccuracy)
                    .Add("冷端补偿", InternalColdJunction ? "内置" : "外置");
            }

            // 频率
            builder.AddGroup("测量功能-频率")
                .Add("频率范围", "20Hz ~ 1MHz")
                .Add("周期范围", "1μs ~ 50ms")
                .Add("精度", FreqAccuracy)
                .Add("灵敏度", "100mVrms");

            // 其他
            builder.AddGroup("测量功能-其他")
                .Add("二极管测试", "0V ~ 5V")
                .Add("通断测试", "<200Ω 蜂鸣");
        }

        /// <summary>
        /// 添加DMM性能指标
        /// </summary>
        private void AddDmmPerformanceSpecs(DeviceSpecificationBuilder builder)
        {
            builder.AddGroup("性能指标")
                .Add("读数速率", "慢速:2.5/中速:50/快速:10K rdgs/s")
                .Add("温度系数", "0.1×(精度)/°C")
                .Add("CMRR", ">60dB (DC, 1kΩ不平衡)")
                .Add("NMRR", ">60dB (50/60Hz)");
        }

        /// <summary>
        /// 添加DMM数学功能
        /// </summary>
        private void AddDmmMathFunctionSpecs(DeviceSpecificationBuilder builder)
        {
            if (!string.IsNullOrEmpty(MathFunctions))
            {
                builder.AddGroup("数学功能")
                    .Add("统计功能", "Min/Max/Avg/σ")
                    .Add("比较功能", "P/F判定, Limit设置")
                    .Add("运算功能", "dBm/dB/Rel/Null")
                    .Add("保持功能", "Hold");
            }
        }

        /// <summary>
        /// 添加DMM触发功能
        /// </summary>
        private void AddDmmTriggerSpecs(DeviceSpecificationBuilder builder)
        {
            builder.AddGroup("触发功能")
                .Add("触发方式", "Auto/Manual/Bus/External")
                .Add("积分时间", IntegrationTime)
                .Add("触发延迟", "0 ~ 1小时");
        }

        /// <summary>
        /// 添加DMM通信接口
        /// </summary>
        private void AddDmmCommunicationSpecs(DeviceSpecificationBuilder builder)
        {
            builder.AddGroup("通信接口")
                .AddIf(InterfaceUSB, "USB", "USB Device (USBTMC)")
                .AddIf(InterfaceLAN, "LAN", "10/100M以太网 (LXI-C)")
                .AddIf(InterfaceRS232, "RS-232", "9600~115200 bps")
                .AddIf(InterfaceGPIB, "GPIB", "IEEE-488.2 (选配)")
                .AddIfNotEmpty("协议", ProtocolSupport);
        }

        /// <summary>
        /// 添加DMM功能特性
        /// </summary>
        private void AddDmmFeatureSpecs(DeviceSpecificationBuilder builder)
        {
            builder.AddGroup("功能特性")
                .AddIf(ArbitrarySensorSupport, "任意传感器", "支持自定义传感器")
                .AddIf(CloneFunction, "克隆功能", "支持U盘参数克隆")
                .AddIf(TrendChart, "趋势图", "实时显示测量趋势")
                .AddIf(Histogram, "直方图", "统计分布显示")
                .AddIfNotEmpty("配套软件", PcSoftware);
        }

        /// <summary>
        /// 添加DMM环境参数
        /// </summary>
        private void AddDmmEnvironmentSpecs(DeviceSpecificationBuilder builder)
        {
            builder.AddGroup("环境参数")
                .Add("工作温度", "0 ~ 50°C")
                .Add("存储温度", "-20 ~ 70°C")
                .Add("相对湿度", "≤80% (非凝结)")
                .Add("电源", "AC 100~240V, 50/60Hz")
                .Add("功耗", "<50W");
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 简化为单个测量功能节点
            var measureNode = new DmmMeasureFunctionNode
            {
                Name = "测量功能",
                ParentNode = "数字万用表",
                Model = "电压 / 电流 / 电阻 / 频率",
                SlotPosition = "DMM",
                Status = Constants.DeviceConstants.Status.Normal
            };
            Children.Add(measureNode);
        }

        // 保留原有的详细初始化方法（如果需要切换回来）
        private void InitializeDetailedChildren()
        {
            Children.Clear();

            // 创建各测量类型通道
            // 1. DC电压通道（5个量程）
            Children.Add(new DmmDcvChannelNode
            {
                Name = "DC电压",
                ParentNode = "数字万用表",
                RangeName = "200mV~1000V",
                Ranges = "200mV/2V/20V/200V/1000V",
                Accuracy = DcvAccuracy,
                InputImpedance = ">10GΩ (<200V), 10MΩ (1000V)",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 2. AC电压通道（5个量程）
            Children.Add(new DmmAcvChannelNode
            {
                Name = "AC电压",
                ParentNode = "数字万用表",
                RangeName = "200mV~750V",
                Ranges = "200mV/2V/20V/200V/750V",
                Accuracy = AcvAccuracy,
                FrequencyRange = "20Hz ~ 100kHz",
                TrueRMS = TrueRmsSupport,
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 3. DC电流通道（5个量程）
            Children.Add(new DmmDciChannelNode
            {
                Name = "DC电流",
                ParentNode = "数字万用表",
                RangeName = "200μA~10A",
                Ranges = "200μA/2mA/20mA/200mA/10A",
                Accuracy = DciAccuracy,
                MaxInput = "10A (连续)",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 4. AC电流通道（3个量程）
            Children.Add(new DmmAciChannelNode
            {
                Name = "AC电流",
                ParentNode = "数字万用表",
                RangeName = "20mA~10A",
                Ranges = "20mA/200mA/10A",
                Accuracy = AciAccuracy,
                FrequencyRange = "20Hz ~ 10kHz",
                TrueRMS = TrueRmsSupport,
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 5. 2线电阻通道
            Children.Add(new DmmResChannelNode
            {
                Name = "2线电阻",
                ParentNode = "数字万用表",
                RangeName = "200Ω~100MΩ",
                Ranges = "200Ω/2kΩ/20kΩ/200kΩ/2MΩ/20MΩ/100MΩ",
                Accuracy = "0.02% + 0.002%",
                WireMode = "2线",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 6. 4线电阻通道
            Children.Add(new DmmResChannelNode
            {
                Name = "4线电阻",
                ParentNode = "数字万用表",
                RangeName = "200Ω~100MΩ",
                Ranges = "200Ω/2kΩ/20kΩ/200kΩ/2MΩ/20MΩ/100MΩ",
                Accuracy = ResAccuracy,
                WireMode = "4线",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 7. 电容通道
            Children.Add(new DmmCapChannelNode
            {
                Name = "电容",
                ParentNode = "数字万用表",
                RangeName = "2nF~200μF",
                Ranges = "2nF/20nF/200nF/2μF/20μF/200μF",
                Accuracy = CapAccuracy,
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 8. 频率通道
            Children.Add(new DmmFreqChannelNode
            {
                Name = "频率",
                ParentNode = "数字万用表",
                RangeName = "20Hz~1MHz",
                FrequencyRange = "20Hz ~ 1MHz",
                Accuracy = FreqAccuracy,
                Sensitivity = "100mVrms",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 9. 周期通道
            Children.Add(new DmmFreqChannelNode
            {
                Name = "周期",
                ParentNode = "数字万用表",
                RangeName = "1μs~50ms",
                FrequencyRange = "1μs ~ 50ms",
                Accuracy = FreqAccuracy,
                Sensitivity = "100mVrms",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 10. 温度通道 - 热电偶
            Children.Add(new DmmTempChannelNode
            {
                Name = "温度-热电偶",
                ParentNode = "数字万用表",
                RangeName = "-200°C~+1820°C",
                SensorType = "TC (B/E/J/K/N/R/S/T)",
                TemperatureRange = "-200°C ~ +1820°C (取决于类型)",
                Accuracy = TempAccuracy,
                ColdJunction = InternalColdJunction ? "内置" : "外置",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 11. 温度通道 - RTD
            Children.Add(new DmmTempChannelNode
            {
                Name = "温度-RTD",
                ParentNode = "数字万用表",
                RangeName = "-200°C~+800°C",
                SensorType = "RTD (Pt100/Pt385/Cu10/Cu50)",
                TemperatureRange = "-200°C ~ +800°C",
                Accuracy = "0.5°C",
                ColdJunction = "N/A",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 12. 温度通道 - 热敏电阻
            Children.Add(new DmmTempChannelNode
            {
                Name = "温度-热敏电阻",
                ParentNode = "数字万用表",
                RangeName = "-80°C~+150°C",
                SensorType = "THER (2252Ω/5000Ω/10000Ω)",
                TemperatureRange = "-80°C ~ +150°C",
                Accuracy = "0.2°C",
                ColdJunction = "N/A",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 13. 二极管通道
            Children.Add(new DmmDiodeChannelNode
            {
                Name = "二极管",
                ParentNode = "数字万用表",
                RangeName = "0V~5V",
                TestVoltage = "0V ~ 5V",
                TestCurrent = "1mA",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });

            // 14. 通断通道
            Children.Add(new DmmDiodeChannelNode
            {
                Name = "通断",
                ParentNode = "数字万用表",
                RangeName = "<200Ω",
                TestVoltage = "< 200Ω 蜂鸣",
                TestCurrent = "1mA",
                SlotPosition = "N/A",
                Status = Constants.DeviceConstants.Status.Normal
            });
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // 使用FromDevice静态方法创建主设备信息项
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有测量类型子节点信息
            foreach (var child in Children)
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(child, true);
                if (subNodeInfo != null)
                {
                    items.Add(subNodeInfo);
                }
            }

            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   Resolution > 0 &&
                   DisplayCount > 0;
        }
    }

    #region DMM 测量类型子节点类

    /// <summary>
    /// DMM 测量功能节点（简化版）
    /// </summary>
    public class DmmMeasureFunctionNode : SubNodeBase
    {
        public override string DeviceTypeName => "测量功能";

        public DmmMeasureFunctionNode() : base("测量功能", "数字万用表")
        {
            SlotPosition = "DMM";
        }

        public override string GetConnectionString()
        {
            return $"DMM_Function::Measure";
        }
    }

    /// <summary>
    /// DC电压通道节点
    /// </summary>
    public class DmmDcvChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;
        private string _inputImpedance;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        public override string DeviceTypeName => "DC电压";

        public DmmDcvChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_DCV::{RangeName}";
        }
    }

    /// <summary>
    /// AC电压通道节点
    /// </summary>
    public class DmmAcvChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;
        private string _frequencyRange;
        private bool _trueRMS;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        public bool TrueRMS
        {
            get => _trueRMS;
            set => SetProperty(ref _trueRMS, value);
        }

        public override string DeviceTypeName => "AC电压";

        public DmmAcvChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_ACV::{RangeName}";
        }
    }

    /// <summary>
    /// DC电流通道节点
    /// </summary>
    public class DmmDciChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;
        private string _maxInput;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string MaxInput
        {
            get => _maxInput;
            set => SetProperty(ref _maxInput, value);
        }

        public override string DeviceTypeName => "DC电流";

        public DmmDciChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_DCI::{RangeName}";
        }
    }

    /// <summary>
    /// AC电流通道节点
    /// </summary>
    public class DmmAciChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;
        private string _frequencyRange;
        private bool _trueRMS;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        public bool TrueRMS
        {
            get => _trueRMS;
            set => SetProperty(ref _trueRMS, value);
        }

        public override string DeviceTypeName => "AC电流";

        public DmmAciChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_ACI::{RangeName}";
        }
    }

    /// <summary>
    /// 电阻通道节点（2线/4线）
    /// </summary>
    public class DmmResChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;
        private string _wireMode;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string WireMode
        {
            get => _wireMode;
            set => SetProperty(ref _wireMode, value);
        }

        public override string DeviceTypeName => $"{WireMode}电阻";

        public DmmResChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_RES_{WireMode}::{RangeName}";
        }
    }

    /// <summary>
    /// 电容通道节点
    /// </summary>
    public class DmmCapChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _ranges;
        private string _accuracy;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string Ranges
        {
            get => _ranges;
            set => SetProperty(ref _ranges, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public override string DeviceTypeName => "电容";

        public DmmCapChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_CAP::{RangeName}";
        }
    }

    /// <summary>
    /// 频率/周期通道节点
    /// </summary>
    public class DmmFreqChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _frequencyRange;
        private string _accuracy;
        private string _sensitivity;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string Sensitivity
        {
            get => _sensitivity;
            set => SetProperty(ref _sensitivity, value);
        }

        public override string DeviceTypeName => Name;

        public DmmFreqChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_FREQ::{RangeName}";
        }
    }

    /// <summary>
    /// 温度通道节点
    /// </summary>
    public class DmmTempChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _sensorType;
        private string _temperatureRange;
        private string _accuracy;
        private string _coldJunction;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string SensorType
        {
            get => _sensorType;
            set => SetProperty(ref _sensorType, value);
        }

        public string TemperatureRange
        {
            get => _temperatureRange;
            set => SetProperty(ref _temperatureRange, value);
        }

        public string Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public string ColdJunction
        {
            get => _coldJunction;
            set => SetProperty(ref _coldJunction, value);
        }

        public override string DeviceTypeName => Name;

        public DmmTempChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_TEMP::{SensorType}::{RangeName}";
        }
    }

    /// <summary>
    /// 二极管/通断通道节点
    /// </summary>
    public class DmmDiodeChannelNode : DeviceBase
    {
        private string _rangeName;
        private string _testVoltage;
        private string _testCurrent;

        public string RangeName
        {
            get => _rangeName;
            set => SetProperty(ref _rangeName, value);
        }

        public string TestVoltage
        {
            get => _testVoltage;
            set => SetProperty(ref _testVoltage, value);
        }

        public string TestCurrent
        {
            get => _testCurrent;
            set => SetProperty(ref _testCurrent, value);
        }

        public override string DeviceTypeName => Name;

        public DmmDiodeChannelNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, RangeName, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"DMM_DIODE::{RangeName}";
        }
    }

    #endregion
}

namespace MeasureControl.Models.Devices.Configurators.Dmm
{
    using MeasureControl.Models.Devices.Configurators;
    using MeasureControl.Models.Devices;

    /// <summary>
    /// DM3068配置器（已合并到 DmmDevice 文件）
    /// </summary>
    public class DM3068Configurator : DeviceConfiguratorBase
    {
        protected override string[] SupportedModelKeywords => new[] { "dm3068", "dm-3068", "dpm8605", "dpm-8605", "dpm8605-485" };

        public override void Configure(DeviceBase device)
        {
            var dmm = device as DmmDevice;
            if (dmm == null) return;

            // 基本参数
            dmm.Resolution = 6.5;
            dmm.DisplayCount = 2200000;
            dmm.SamplingRate = 10000;  // 快速模式：10K rdgs/s
            dmm.StorageCapacity = 512000;  // 512K rdgs

            // 测量功能
            dmm.SupportedMeasurements = "DCV/ACV/DCI/ACI/2线电阻/4线电阻/电容/二极管/通断/频率/周期/温度";
            dmm.CurrentMeasurementMode = DmmMeasurementType.DCV;
            dmm.TrueRmsSupport = true;

            // 精度参数（典型值）
            dmm.DcvAccuracy = "0.012% + 0.003%";
            dmm.AcvAccuracy = "0.3% + 0.03% (20Hz~1kHz)";
            dmm.DciAccuracy = "0.05% + 0.005%";
            dmm.AciAccuracy = "0.4% + 0.04% (20Hz~1kHz)";
            dmm.ResAccuracy = "0.01% + 0.001% (4线)";
            dmm.CapAccuracy = "1% + 0.3%";
            dmm.FreqAccuracy = "0.01%";
            dmm.TempAccuracy = "1°C (热电偶K型)";

            // 温度测量
            dmm.SupportedTempSensors = "TC(B/E/J/K/N/R/S/T), RTD(Pt100/Pt385/Cu10/Cu50), THER(2252Ω/5000Ω/10000Ω)";
            dmm.InternalColdJunction = true;

            // 数学功能
            dmm.MathFunctions = "Min/Max/Avg/dBm/dB/Rel/Null/P/F/Limit/Hold";

            // 触发功能
            dmm.TriggerMode = DmmTriggerMode.Auto;
            dmm.IntegrationTime = "0.001PLC ~ 100PLC";

            // 通信接口
            dmm.InterfaceUSB = true;
            dmm.InterfaceLAN = true;
            dmm.InterfaceRS232 = true;
            dmm.InterfaceGPIB = true;
            dmm.ProtocolSupport = "USBTMC, LXI-C, SCPI";

            // 显示功能
            dmm.DualDisplay = true;
            dmm.TrendChart = true;
            dmm.Histogram = true;

            // 其他功能
            dmm.ArbitrarySensorSupport = true;
            dmm.CloneFunction = true;
            dmm.PcSoftware = "Ultra Sigma";
        }
    }
}