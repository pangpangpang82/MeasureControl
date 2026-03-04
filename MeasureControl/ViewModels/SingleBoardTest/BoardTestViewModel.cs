using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MeasureControl.Events;
using MeasureControl.Views.SingleBoardTest.AirController;
using MeasureControl.Views.SingleBoardTest.HydraulicController;
using MeasureControl.Views.SingleBoardTest.FuelController;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.SingleBoardTest
{
    public class BoardTestViewModel : BindableBase, INavigationAware
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly MeasureControl.Services.ISingleBoardTestContextService _singleBoardTestContext;
        private const string CommonBoardTypeKey = "Common";

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Func<UserControl>>> TestItemViewFactoriesByBoardType =
            new Dictionary<string, IReadOnlyDictionary<string, Func<UserControl>>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "空气单板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源对地阻抗检查", () => new PowerToGroundImpedanceTestView() },
                        { "控制通道光耦供电测试", () => new AC_6_4CommTabView() },
                        { "控制通道GND/OC离散输入通道输入测试", () => new GndOcDiscreteInputTestView() },
                        { "GND/OC型100mA离散输出通道3输出测试", () => new GndOcDiscreteOutputCh3TestView() },
                        { "GND/OC型100mA离散输出通道2输出测试", () => new GndOcDiscreteOutputCh2TestView() },
                        { "A控制通道28V/OC型100mA离散输出通道1输出测试", () => new A28vOc100mADiscreteOutputCh1TestView() },
                        { "A控制通道28V/OC型100mA离散输出通道2输出测试", () => new A28vOc100mADiscreteOutputCh2TestView() },
                        { "A控制通道28V/OC型400mA离散输出通道1输出测试", () => new A28vOc400mADiscreteOutputCh1TestView() },
                        { "A控制通道28V/OC型400mA离散输出通道2输出测试", () => new A28vOc400mADiscreteOutputCh2TestView() },
                        { "A控制通道28V/OC型400mA离散输出通道3输出测试", () => new A28vOc400mADiscreteOutputCh3TestView() },
                        { "PT500型温度传感器测试", () => new PT500TemperatureSensorCommTabView() },
                        { "6.8.2控制通道MIXTS传感器测试", () => new R_6_8_2View() },
                        { "6.8.3控制通道CAR_TS传感器测试", () => new R_6_8_3View() },
                        { "6.8.4控制通道CKPT_DTS传感器测试", () => new R_6_8_4View() },
                        { "6.8.5控制通道CAB_DTS传感器测试", () => new R_6_8_5View() },
                        { "6.8.6控制通道CAR_DTS传感器测试", () => new R_6_8_6View() },
                        { "6.8.7控制通道BTS传感器测试", () => new R_6_8_7View() },
                        { "6.8.8控制通道PTS传感器测试", () => new R_6_8_8View() },
                        { "6.8.9控制通道CDTS传感器测试", () => new R_6_8_9View() },
                        { "6.5.1.1控制通道ARINC429发送通道1测试", () => new A_C_6_5_1_1View() },
                        { "6.5.1.2A控制通道ARINC429发送通道2/B控制通道ARINC429接收通道5测试", () => new A_C_6_5_1_2View() },
                        { "6.5.2.1A控制通道ARINC接收通道1测试", () => new A_C_6_5_2_1View() },
                        { "6.5.2.2A控制通道ARINC接收通道2测试", () => new A_C_6_5_2_2View() },
                        { "6.5.2.3A控制通道ARINC接收通道3测试", () => new A_C_6_5_2_3View() },
                        { "6.5.2.6A控制通道ARINC接收通道6测试", () => new A_C_6_5_2_6View() },
                        { "8.3.1 S安全通道ARINC429发送通道1测试", () => new S_C_8_3_1View() },
                        { "8.3.2 S安全通道ARINC429接收通道1测试", () => new S_C_8_3_2View() },
                        { "8.3.3 S安全通道ARINC429接收通道2测试", () => new S_C_8_3_3View() },
                        { "电源模块测试", () => new AirSimpleSequenceView("电源模块测试") },
                        { "5V传感器供电电压测试", () => new Pot5VSupplyTestView() },
                        { "A控制通道功率板供电测试", () => new PowerBoardSupplyTestView("A", "A控制通道功率板供电测试") },
                        { "B控制通道功率板供电测试", () => new PowerBoardSupplyTestView("B", "B控制通道功率板供电测试") },
                        { "CAN发送测试", () => new CanCommTestView() },
                        { "CAN接收测试", () => new CanReceiveTestView() },
                        { "安全板CAN测试", () => new AirSimpleSequenceView("安全板CAN测试") },
                        { "RS422通信测试", () => new RS422CommTabView() },
                        { "控制通道422发送测试", () => new RS422Control422TransmitTestView() },
                        { "控制通道422接收测试", () => new RS422Control422ReceiveTestView() },

                        { "6.9.1A控制通道CKPT_VENTS传感器测试", () => new A_C_6_9_1_1View() },
                        { "6.9.2控制通道CAB_VENTS传感器测试", () => new A_C_6_9_2_1View() },
                        { "6.10.1控制通道BMPS压力传感器测试", () => new A_C_6_10_1_1View() },
                        { "6.10.2A控制通道BPS传感器测试", () => new A_C_6_10_2_1View() },
                        { "6.13.1控制通道压力传感器采集测试", () => new A_C_6_13_1_1View() },
                        { "6.13.2 S安全通道压力传感器测试", () => new S_C_6_13_2_1View() },
                        { "6.10.7控制通道RAIA_POS传感器测试", () => new A_C_6_10_7_1View() },
                        { "6.11.1控制通道角度反馈传感器测试", () => new A_C_6_11_1_1View() },
                        { "6.12.1控制通道选气楔传感器测试", () => new A_C_6_12_1_1View() },
                        { "6.15.1.1 A控制通道功率板RAIA直流电机驱动模块速度控制测试", () => new A_C_6_15_1_1View() },
                        { "6.15.1.2 A控制通道功率板RAIA直流电机驱动模块方向控制测试", () => new A_C_6_15_1_2View() },
                        { "6.16.1.1.1 A控制通道功率板TCV步进电机驱动模块输出测试", () => new A_C_6_16_1_1_1View() },
                        { "6.16.2.1 A控制通道功率板驾驶舱TAV步进电机驱动模块测试", () => new A_C_6_16_1_1_2View() },
                        { "6.16.3.1 A控制通道功率板前后客舱TAV步进电机驱动模块测试", () => new A_C_6_16_3_1View() },
                        { "6.17.4.1 A控制通道功率板前后客舱TAV步进电机驱动模块测试", () => new A_C_6_17_4_1View() },
                        { "6.17.1.2 A控制通道功率板TCV方向控制测试", () => new A_C_6_17_1_2View() },
                        { "6.17.2.2 A控制通道功率板TAV步进电机驱动模块方向测试", () => new A_C_6_17_2_2View() },
                        { "6.17.3.2 A控制通道功率板前后客舱TAV步进电机驱动模块方向测试", () => new A_C_6_17_3_2View() },
                        { "6.17.4.2 A控制通道功率板前货舱TAV步进电机驱动模块方向测试", () => new A_C_6_17_4_2View() },
                        { "6.18.1.1 A控制通道功率板FAV力矩电机驱动测试", () => new A_C_6_18_1_1View() },
                        { "6.18.2.1 A控制通道功率板PRSOV力矩电机驱动测试", () => new A_C_6_18_2_1View() },
                        { "6.18.3.1 A控制通道功率板FCV力矩电机驱动测试", () => new A_C_6_18_3_1View() },
                        { "6.18.4.1 A控制通道功率板VAV力矩电机驱动测试", () => new A_C_6_18_4_1View() },
                        { "6.15.2.1 A控制通道功率板AWV直流电机驱动模块速度控制测试", () => new A_C_6_15_2_1View() },
                        { "6.15.2.2 A控制通道功率板AVV直流电机驱动模块方向控制测试", () => new A_C_6_15_2_2View() },
                    }
                },
                {
                    "液压单板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", () => new HC_6_1() },
                        { "二次电源测试", () => new HC_6_2() },
                        { "温度采集测试", () => new HC_6_3() },
                        { "压力传感器信号采集测试", () => new HC_6_4() },
                        { "压差传感器信号采集测试", () => new HC_6_5() },
                        { "油量传感器信号采集测试", () => new HC_6_6() },
                        { "离散量采集测试", () => new HC_6_7() },
                        { "离散量输出测试", () => new HC_6_8() },
                    }
                },
                {
                    "加放油单板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", () => new Views.SingleBoardTest.FuelController.PowerImpedanceTestView() },
                        { "二次电源测试", () => new SecondaryPowerTestView() },
                        { "低电压告警功能测试", () => new LowVoltageAlarmTestView() },
                        { "温度采集功能", () => new TemperatureAcquisitionTestView() },
                        { "离散量采集功能测试", () => new DiscreteInputTestView() },
                        { "离散量输出功能测试", () => new DiscreteOutputTestView() },
                        { "RS422通信功能测试", () => new RS422CommunicationFunctionTestView() },
                        { "RS422通信自检测功能测试", () => new RS422SelfCheckTestView() },
                    }
                },
                {
                    "惰化单板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", () => new Views.SingleBoardTest.InertController.PowerImpedanceTestView() },
                        { "控制板电源阻抗测试", () => new Views.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestView() },
                        { "控制板离散输入模块测试", () => new Views.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestView() },
                        { "温度传感器信号采集", () => new Views.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestView() },
                        { "压力传感器信号采集", () => new Views.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestView() },
                        { "二次、三次电源测试", () => new Views.SingleBoardTest.InertController.SecondaryTertiaryPowerTestView() },
                        { "电源监控测试", () => new Views.SingleBoardTest.InertController.PowerMonitorTestView() },
                        { "超温切断模块电路测试", () => new Views.SingleBoardTest.InertController.OverTemperatureCutoffTestView() },
                        { "锁存模块电路测试", () => new Views.SingleBoardTest.InertController.LatchModuleCircuitTestView() },
                    }
                }
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CriteriaTextsByBoardType =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "液压单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", "\t a) 阻抗值大于500Ω；\r\n \t b) 阻抗值大于500Ω。" },
                        { "二次电源测试", "\t a) 5V隔离二次电源输出电压范围在[4.925，5.075]V；\r\n \t b) 15V隔离二次电源输出电压范围在[14.775，15.225]V；\r\n \t c) -15V隔离二次电源输出电压范围在[-14.775，-15.225]V。" },
                        { "温度采集测试", "\t a) 阻值为763.3±2.0Ω，温度值在[-66.6,-53.4]°C;\r\n \t b) 阻值为763.3±2.0Ω，温度值在[193.4,206.6]°C;\r\n \t c) 阻值为763.3±2.0Ω，温度值在[32.4,46.6]°C。" },
                        { "压力传感器信号采集测试", "\t a) 电压供电0.5±0.0717，压力值在[0,3.4]Psia;\r\n \t b) 电压供电7.17±0.0717V，压力值在[3915,4000]Psia;\r\n \t c) 电压供电3.0±0.0717V，压差力在[1414,1585]Psia。" },
                        { "压差传感器信号采集测试", "\t a) 电流供电4±0.2mA，压力值在[0,85]Psid;\r\n \t b) 电流供电20±0.2mA，压力值在[121.5,128.4]Psid;\r\n \t c) 电流供电10±0.2mA，压力值在[43.44,50.31]Psid。" },
                        { "油量传感器信号采集测试", "\t a);\r\n \t b);\r\n \t c)。" },
                        { "离散量采集测试", "\t 采集结果均为1。" },
                        { "离散量输出测试", "\t a) 置为开路时，针脚9~15对地阻抗均大于100kΩ;\r\n \t b) 置为通路时，针脚9~15对地阻抗均小于10Ω。" },
                    }
                },
                {
                    "加放油单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", "\t a) J3-J4 外部28V对地阻抗大于500Ω；\r\n\t b) J14-J24 内部28V对地阻抗大于500Ω；\r\n\t c) J3-J5 外部28V对壳体阻抗大于500Ω；\r\n\t d) J14-J5 内部28V对壳体阻抗大于500Ω。" },
                        { "二次电源测试", "\t CRM_PIN1对CRM_PIN18之间的直流电压在[4.5V, 5.5V]范围内为PASS。" },
                        { "低电压告警功能测试", "\t 供电电压从17V下降过程中，CRM_PIN3电平在供电电压低于15V之前发生翻转为PASS。" },
                        { "温度采集功能", "\t 通过DS18B20温度传感器解析CRM_PIN7信号，温度值在[15℃, 45℃]区间内为PASS。" },
                        { "离散量采集功能测试", "\t a) 接地测试：所有DO通道接地时，DI采集结果均为1；\r\n\t b) 开路测试：所有DO通道开路时，DI采集结果均为0。" },
                        { "离散量输出功能测试", "\t a) DO接地时，对地阻抗小于10Ω；\r\n\t b) DO开路时，对地阻抗大于100kΩ；\r\n\t c) 28V上电后，J14电压不低于16V。" },
                        { "RS422通信功能测试", "\t a/b/c/d四个步骤：发送0xAA 55，接收数据与发送数据一致为PASS。" },
                        { "RS422通信自检测功能测试", "\t a) CRM_PIN9发送，CRM_PIN19接收，回环数据一致为PASS；\r\n\t b) CRM_PIN10发送，CRM_PIN20接收，回环数据一致为PASS。" },
                    }
                },
                {
                    "惰化单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", "\t 测量阻抗值大于500Ω则合格。" },
                        { "控制板电源阻抗测试", "\t 控制板下电；测量J1、J2、J4、J5到J18（COM）、J70（EARTH）之间的阻抗，阻抗值大于500Ω则合格。" },
                        { "控制板离散输入模块测试", "\t 控制板供电28V；将引脚J40-J45、J75-J83分别配置为GND和开路，通过通信读取采集结果；将引脚J84、J85分别配置为28V和开路，通过通信读取采集结果。采集结果应与配置状态一致。" },
                        { "温度传感器信号采集", "\t 控制板供电28V；按表7-2配置PT500A/PT500B/PT1000A/PT1000B模拟电阻值，通过通信读取换算温度；上位机读取温度满足表7-3与表7-4为合格。" },
                        { "压力传感器信号采集", "\t 通过引脚J25、J26将“压力传感器”的模拟电压按表7-5进行设置，通过通讯读取的“压力”的数值；上位机读取的“压力”数据显示的值满足表7-6为合格。" },
                        { "二次、三次电源测试", "\t a) 15V检测：15±1.5V；\r\n \t b) -15V检测：-15±1.5V；\r\n \t c) 5V检测：5±0.5V；\r\n \t d) 3.3V检测：3.3±0.33V。" },
                        { "电源监控测试", "\t a) 供电28V：J86-J91电压为（2.46±0.24）V；\r\n \t b) 供电18V：J86-J91电压为（1.56±0.15）V；\r\n \t c) 供电32V：J86-J91电压为（2.81±0.28）V。" },
                        { "超温切断模块电路测试", "\t a) PT500A电阻配置为（715.25±3.5）Ω：J31(T1_AWARN)输出高电平(3.3±0.33V)，J11(IIV +28VDC PWR IN_FB)开路(≤16V)，J12(IIV +28VDC PWR IN)开路(≤16V)。\r\n \t b) PT1000A电阻配置为（1411.6±7.1）Ω：J32(T2_AWARN)输出高电平(3.3±0.33V)，J13(TIV +28VDC PWR IN_FB)开路(≤16V)，J14(TIV +28VDC PWR IN)开路(≤16V)。" },
                        { "锁存模块电路测试", "\t a) PT500A=730Ω：J31输出为高电平(3.3±0.33V)；\r\n \t b) PT500A降低为500Ω：J31输出仍为高电平(3.3±0.33V)；\r\n \t c) J34供电3.3V后：J31输出为低电平(0±0.1V)；\r\n \t d) PT1000A=1500Ω：J32输出为高电平(3.3±0.33V)；\r\n \t e) PT1000A降低为1000Ω：J32输出仍为高电平(3.3±0.33V)；\r\n \t f) J35供电3.3V后：J32输出为低电平(0±0.1V)。" },
                    }
                },
                {
                    "空气单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源对地阻抗检查", "\t 电源的对地阻抗应不小于200Ω。" },
                        { "A控制通道28V/OC型100mA离散输出通道1输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "A控制通道28V/OC型100mA离散输出通道2输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "A控制通道28V/OC型400mA离散输出通道1输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "A控制通道28V/OC型400mA离散输出通道2输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "A控制通道28V/OC型400mA离散输出通道3输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.13.1控制通道压力传感器采集测试", "\t a) 测试点1：输入1100±1mbar，采集压力在[1095.21,1104.79]mbar；\r\n\t b) 测试点2：输入1500±1mbar，采集压力在[1496.21,1504.79]mbar；\r\n\t c) 测试点3：输入2000±1mbar，采集压力在[1995.21,2004.79]mbar。" },
                        { "6.13.2 S安全通道压力传感器测试", "\t a) 测试点1：输入1100±1mbar，采集压力在[1082,1118]mbar；\r\n\t b) 测试点2：输入1500±1mbar，采集压力在[1482,1518]mbar；\r\n\t c) 测试点3：输入2000±1mbar，采集压力在[1982,2018]mbar。" },
                    }
                }
            };

        private string _testTaskName;
        private string _boardType;
        private string _parentChassisName;
        private string _pageKey;
        private object _rightPanelContent;
        private TestSequenceItem _selectedTestItem;
        private string _selectedTestCriteriaText;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public string BoardType
        {
            get => _boardType;
            set => SetProperty(ref _boardType, value);
        }

        public string ParentChassisName
        {
            get => _parentChassisName;
            set => SetProperty(ref _parentChassisName, value);
        }

        public string PageKey
        {
            get => _pageKey;
            private set => SetProperty(ref _pageKey, value);
        }

        public DelegateCommand CloseInRegionCommand { get; }

        public BoardTestViewModel(IEventAggregator eventAggregator, MeasureControl.Services.ISingleBoardTestContextService singleBoardTestContext)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _singleBoardTestContext = singleBoardTestContext ?? throw new ArgumentNullException(nameof(singleBoardTestContext));
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
        }

        public ObservableCollection<TestSequenceItem> TestSequenceItems { get; } = new ObservableCollection<TestSequenceItem>();

        public string TestCriteriaTitle => "测试判据";

        public double TestCriteriaFontSize => 15;

        public double TestCriteriaLineHeight => 30;

        public string SelectedTestCriteriaText
        {
            get => _selectedTestCriteriaText;
            private set => SetProperty(ref _selectedTestCriteriaText, value);
        }

        public TestSequenceItem SelectedTestItem
        {
            get => _selectedTestItem;
            set
            {
                if (SetProperty(ref _selectedTestItem, value))
                {
                    if (value == null)
                    {
                        RightPanelContent = null;
                        SelectedTestCriteriaText = string.Empty;
                        return;
                    }

                    SelectedTestCriteriaText = ResolveCriteriaText(BoardType, value.Name);

                    if (TryGetViewFactory(BoardType, value.Name, out var viewFactory))
                    {
                        RightPanelContent = viewFactory();
                        return;
                    }

                    RightPanelContent = new TextBlock { Text = value.Name, Margin = new System.Windows.Thickness(10) };
                }
            }
        }

        public object RightPanelContent
        {
            get => _rightPanelContent;
            set => SetProperty(ref _rightPanelContent, value);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var parameters = navigationContext?.Parameters;
            TestTaskName = parameters?.GetValue<string>("TestTaskName") ?? string.Empty;
            BoardType = parameters?.GetValue<string>("BoardType") ?? string.Empty;
            ParentChassisName = parameters?.GetValue<string>("ParentChassisName")
                                ?? parameters?.GetValue<string>("ChassisName")
                                ?? string.Empty;

            _singleBoardTestContext.Update(ParentChassisName, TestTaskName, BoardType);

            var instanceId = string.IsNullOrWhiteSpace(ParentChassisName) ? TestTaskName : $"{ParentChassisName}-{TestTaskName}";
            PageKey = string.IsNullOrWhiteSpace(instanceId) ? "BoardTest" : $"BoardTest_{instanceId}";

            LoadFixedTestItems(BoardType);

            SelectedTestItem = TestSequenceItems.FirstOrDefault();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        private static bool TryGetViewFactory(string boardType, string testItemName, out Func<UserControl> viewFactory)
        {
            viewFactory = null;

            if (!string.IsNullOrWhiteSpace(boardType)
                && TestItemViewFactoriesByBoardType.TryGetValue(boardType, out var perBoard)
                && perBoard != null
                && perBoard.TryGetValue(testItemName, out viewFactory))
            {
                return true;
            }

            if (TestItemViewFactoriesByBoardType.TryGetValue(CommonBoardTypeKey, out var common)
                && common != null
                && common.TryGetValue(testItemName, out viewFactory))
            {
                return true;
            }

            viewFactory = null;
            return false;
        }

        private static string ResolveCriteriaText(string boardType, string testItemName)
        {
            if (string.IsNullOrWhiteSpace(boardType) || string.IsNullOrWhiteSpace(testItemName))
            {
                return string.Empty;
            }

            if (CriteriaTextsByBoardType.TryGetValue(boardType, out var perBoard)
                && perBoard != null
                && perBoard.TryGetValue(testItemName, out var text))
            {
                return text ?? string.Empty;
            }

            return string.Empty;
        }

        private void LoadFixedTestItems(string boardType)
        {
            TestSequenceItems.Clear();

            if (string.Equals(boardType, "空气单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源对地阻抗检查"));
                TestSequenceItems.Add(new TestSequenceItem("电源模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道功率板供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("B控制通道功率板供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("PT500型温度传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.2控制通道MIXTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.3控制通道CAR_TS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.4控制通道CKPT_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.5控制通道CAB_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.6控制通道CAR_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.7控制通道BTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.8控制通道PTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.9控制通道CDTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("5V传感器供电电压测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道光耦供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.1.1控制通道ARINC429发送通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.1.2A控制通道ARINC429发送通道2/B控制通道ARINC429接收通道5测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.1A控制通道ARINC接收通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.2A控制通道ARINC接收通道2测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.3A控制通道ARINC接收通道3测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.6A控制通道ARINC接收通道6测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.9.1A控制通道CKPT_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.9.2控制通道CAB_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.1控制通道BMPS压力传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.2A控制通道BPS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.13.1控制通道压力传感器采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.13.2 S安全通道压力传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.7控制通道RAIA_POS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.11.1控制通道角度反馈传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.12.1控制通道选气楔传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.1.1 A控制通道功率板RAIA直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.1.2 A控制通道功率板RAIA直流电机驱动模块方向控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.16.1.1.1 A控制通道功率板TCV步进电机驱动模块输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.16.2.1 A控制通道功率板驾驶舱TAV步进电机驱动模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.16.3.1 A控制通道功率板前后客舱TAV步进电机驱动模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.17.4.1 A控制通道功率板前后客舱TAV步进电机驱动模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.17.1.2 A控制通道功率板TCV方向控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.17.2.2 A控制通道功率板TAV步进电机驱动模块方向测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.17.3.2 A控制通道功率板前后客舱TAV步进电机驱动模块方向测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.17.4.2 A控制通道功率板前货舱TAV步进电机驱动模块方向测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.18.1.1 A控制通道功率板FAV力矩电机驱动测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.18.2.1 A控制通道功率板PRSOV力矩电机驱动测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.18.3.1 A控制通道功率板FCV力矩电机驱动测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.18.4.1 A控制通道功率板VAV力矩电机驱动测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.2.1 A控制通道功率板AWV直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.2.2 A控制通道功率板AVV直流电机驱动模块方向控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.1 S安全通道ARINC429发送通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.2 S安全通道ARINC429接收通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.3 S安全通道ARINC429接收通道2测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道GND/OC离散输入通道输入测试"));
                TestSequenceItems.Add(new TestSequenceItem("GND/OC型100mA离散输出通道3输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("GND/OC型100mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道28V/OC型100mA离散输出通道1输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道28V/OC型100mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道28V/OC型400mA离散输出通道1输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道28V/OC型400mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("A控制通道28V/OC型400mA离散输出通道3输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("ARINC429通讯测试"));
                TestSequenceItems.Add(new TestSequenceItem("CAN发送测试"));
                TestSequenceItems.Add(new TestSequenceItem("CAN接收测试"));
                TestSequenceItems.Add(new TestSequenceItem("安全板CAN测试"));
                TestSequenceItems.Add(new TestSequenceItem("RS422通信测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道422发送测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道422接收测试"));
                return;
            }

            if (string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("二次电源测试"));
                TestSequenceItems.Add(new TestSequenceItem("温度采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("压力传感器信号采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("压差传感器信号采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("油量传感器信号采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("离散量采集测试"));
                TestSequenceItems.Add(new TestSequenceItem("离散量输出测试"));
            }

            if (string.Equals(boardType, "惰化单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制板电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制板离散输入模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("温度传感器信号采集"));
                TestSequenceItems.Add(new TestSequenceItem("压力传感器信号采集"));
                TestSequenceItems.Add(new TestSequenceItem("二次、三次电源测试"));
                TestSequenceItems.Add(new TestSequenceItem("电源监控测试"));
                TestSequenceItems.Add(new TestSequenceItem("超温切断模块电路测试"));
                TestSequenceItems.Add(new TestSequenceItem("锁存模块电路测试"));
                return;
            }

            if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("二次电源测试"));
                TestSequenceItems.Add(new TestSequenceItem("低电压告警功能测试"));
                TestSequenceItems.Add(new TestSequenceItem("温度采集功能"));
                TestSequenceItems.Add(new TestSequenceItem("离散量采集功能测试"));
                TestSequenceItems.Add(new TestSequenceItem("离散量输出功能测试"));
                TestSequenceItems.Add(new TestSequenceItem("RS422通信功能测试"));
                TestSequenceItems.Add(new TestSequenceItem("RS422通信自检测功能测试"));
                return;
            }
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭单板测试吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _eventAggregator.GetEvent<ReleaseCurrentPageEvent>().Publish(PageKey);
            }
        }

        public class TestSequenceItem
        {
            public TestSequenceItem(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }
}
