using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MeasureControl.Events;
using MeasureControl.Services;
using MeasureControl.Views.SingleBoardTest.AirController;
using MeasureControl.Views.SingleBoardTest.HydraulicController;
using MeasureControl.Views.SingleBoardTest.FuelController;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using System.Windows;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest
{
    internal enum CurrentTestState
    {
        Idle,
        Running,
        Stopping
    }

    public class BoardTestViewModel : BindableBase, INavigationAware, IConfirmNavigationRequest, ICloseGuard
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IHydraulicPowerService _hydraulicPowerService;
        private const string CommonBoardTypeKey = "Common";
        private bool _isStoppingCurrentTest;

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Func<UserControl>>> TestItemViewFactoriesByBoardType =
            new Dictionary<string, IReadOnlyDictionary<string, Func<UserControl>>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "空气单板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "6.1电源对地阻抗检查", () => new PowerToGroundImpedanceTestView() },
                        { "8.1电源对地阻抗测试", () => new A_C_8_1View() },
                        { "7.1功率板电源对地阻抗测试", () => new A_C_7_1View() },
                        { "6.4控制通道光耦供电测试", () => new AC_6_4CommTabView() },
                        { "6.14.1控制通道GND/OC离散输入通道输入测试", () => new GndOcDiscreteInputTestView() },
                        { "6.15.1.1GND/OC型离散输出通道3输出测试", () => new GndOcDiscreteOutputCh3TestView() },
                        { "6.15.1.2GND/OC型100mA离散输出通道2输出测试", () => new GndOcDiscreteOutputCh2TestView() },
                        { "6.15.2.1A控制通道28V/OC型100mA离散输出通道1输出测试", () => new A28vOc100mADiscreteOutputCh1TestView() },
                        { "6.15.2.2A控制通道28V/OC型100mA离散输出通道2输出测试", () => new A28vOc100mADiscreteOutputCh2TestView() },
                        { "6.15.3.1A控制通道28V/OC型400mA离散输出通道1输出测试", () => new A28vOc400mADiscreteOutputCh1TestView() },
                        { "6.15.3.2A控制通道28V/OC型400mA离散输出通道2输出测试", () => new A28vOc400mADiscreteOutputCh2TestView() },
                        { "6.15.3.3A控制通道28V/OC型400mA离散输出通道3输出测试", () => new A28vOc400mADiscreteOutputCh3TestView() },
                        { "6.8.1控制通道PDTS传感器测试", () => new PT500TemperatureSensorCommTabView() },
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
                        { "6.5.2.4A控制通道ARINC接收通道4测试", () => new A_C_6_5_2_4View() },
                        { "6.5.2.5A控制通道ARINC接收通道5测试", () => new A_C_6_5_2_5View() },
                        { "6.5.2.6A控制通道ARINC接收通道6测试", () => new A_C_6_5_2_6View() },
                        { "8.3.1 S安全通道ARINC429发送通道1测试", () => new S_C_8_3_1View() },
                        { "8.3.2 S安全通道ARINC429接收通道1测试", () => new S_C_8_3_2View() },
                        { "8.3.3 S安全通道ARINC429接收通道2测试", () => new S_C_8_3_3View() },
                        { "8.10.1 S通道OFV/TRV直流电机驱动模块速度控制测试", () => new S_C_8_10_1View() },
                        { "7.3.1.1.2 A控制通道功率板RAIA直流电机驱动模块供电电压测试", () => new A_C_7_3_1_1_2View() },
                        { "7.3.1.2 A控制通道功率板RATA直流电机驱动模块速度控制测试", () => new A_C_7_3_1_2View() },
                        { "7.3.2.2 A控制通道功率板AVV直流电机驱动模块速度控制测试", () => new A_C_7_3_2_2View() },
                        { "7.3.3.2 B控制通道PWM/FPGA速度控制测试", () => new B_C_7_3_3_2View() },
                        { "电源模块测试", () => new AirSimpleSequenceView("电源模块测试") },
                        { "6.2.1A控制通道供电测试", () => new AirSimpleSequenceView("6.2.1A控制通道供电测试") },
                        { "5V传感器供电电压测试", () => new Pot5VSupplyTestView() },
                        { "6.3 5V传感器供电电压测试", () => new Pot5VSupplyTestView() },
                        { "A控制通道功率板供电测试", () => new PowerBoardSupplyTestView("A", "A控制通道功率板供电测试") },
                        { "7.2.1A控制通道功率板供电测试", () => new PowerBoardSupplyTestView("A", "7.2.1A控制通道功率板供电测试") },
                        { "B控制通道功率板供电测试", () => new PowerBoardSupplyTestView("B", "B控制通道功率板供电测试") },
                        { "7.2.2B控制通道功率板供电测试", () => new PowerBoardSupplyTestView("B", "7.2.2B控制通道功率板供电测试") },
                        { "CAN发送测试", () => new CanCommTestView() },
                        { "6.6.1CAN发送测试", () => new CanCommTestView() },
                        { "CAN接收测试", () => new CanReceiveTestView() },
                        { "6.6.2CAN接收测试", () => new CanReceiveTestView() },
                        { "安全板CAN测试", () => new AirSimpleSequenceView("安全板CAN测试") },
                        { "8.5.1安全通道CAN发送测试", () => new S_C_8_5_1View() },
                        { "8.5.2安全通道CAN接收测试", () => new S_C_8_5_2View() },
                        { "8.6.1 S安全通道WAITS1传感器测试", () => new S_C_8_6_1View() },
                        { "8.6.2 S安全通道WAITS2传感器测试", () => new S_C_8_6_2View() },
                        { "8.7.1S安全通道FWD_AVENTS1传感器测试", () => new S_C_8_7_1View() },
                        { "8.7.2S安全通道FWD_AVENTS2传感器测试", () => new S_C_8_7_2View() },
                        { "8.7.3S安全通道AFT_AVENTS传感器测试", () => new S_C_8_7_3View() },
                        { "RS422通信测试", () => new RS422CommTabView() },
                        { "控制通道422发送测试", () => new RS422Control422TransmitTestView() },
                        { "控制通道422接收测试", () => new RS422Control422ReceiveTestView() },

                        { "6.9.1A控制通道CKPT_VENTS传感器测试", () => new A_C_6_9_1_1View() },
                        { "6.9.2控制通道CAB_VENTS传感器测试", () => new A_C_6_9_2_1View() },
                        { "6.10.1控制通道BMPS压力传感器测试", () => new A_C_6_10_1_1View() },
                        { "6.10.2A控制通道BPS传感器测试", () => new A_C_6_10_2_1View() },
                        { "6.10.3控制通道WAIPSI1传感器测试", () => new A_C_6_10_3_1View() },
                        { "6.10.4控制通道WAIPSI2传感器测试", () => new A_C_6_10_4_1View() },
                        { "6.10.5控制通道PDPS传感器测试", () => new A_C_6_10_5_1View() },
                        { "6.10.6A控制通道PIFS传感器测试", () => new A_C_6_10_6_1View() },
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
                    "惰化模拟板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", () => new Views.SingleBoardTest.InertController.PowerImpedanceTestView() },
                        { "二次、三次电源测试", () => new Views.SingleBoardTest.InertController.SecondaryTertiaryPowerTestView() },
                        { "超温切断模块电路测试", () => new Views.SingleBoardTest.InertController.OverTemperatureCutoffTestView() },
                        { "锁存模块电路测试", () => new Views.SingleBoardTest.InertController.LatchModuleCircuitTestView() },
                    }
                }
                ,
                {
                    "惰化控制板",
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "控制板电源阻抗测试", () => new Views.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestView() },
                        { "控制板二、三次电源测试", () => new Views.SingleBoardTest.InertController.ControlBoardSecondaryTertiaryPowerTestView() },
                        { "控制板离散输入模块测试", () => new Views.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestView() },
                        { "离散输出模块测试", () => new Views.SingleBoardTest.InertController.DiscreteOutputModuleTestView() },
                        { "温度传感器信号采集", () => new Views.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestView() },
                        { "压力传感器信号采集", () => new Views.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestView() },
                        { "氧气传感器信号采集", () => new Views.SingleBoardTest.InertController.OxygenSensorSignalAcquisitionTestView() },
                        { "TCV电机驱动测试", () => new Views.SingleBoardTest.InertController.TcvMotorDriveTestView() },
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
                        { "电源阻抗测试", "\t a) 针脚1和4之间阻抗值大于500Ω；\r\n \t b) 针脚1和82之间阻抗值大于500Ω。" },
                        { "二次电源测试", "\t a) 5V隔离二次电源输出电压范围在[4.82，5.18]V；\r\n \t b) 15V隔离二次电源输出电压范围在[14.47，15.53]V；\r\n \t c) -15V隔离二次电源输出电压范围在[-15.53，-14.47]V。" },
                        { "温度采集测试", "\t a) 阻值为763.3±2.0Ω时，温度值在[-66.6,-53.4]°C;\r\n \t b) 阻值为1758.6±2.0Ω时，温度值在[193.4,206.6]°C;\r\n \t c) 阻值为1155.4±2.0Ω时，温度值在[32.4,46.6]°C。" },
                        { "压力传感器信号采集测试", "\t a) 电压供电0.5±0.0717时，压力值在[0,85]Psi;\r\n \t b) 电压供电7.17±0.0717V时，压力值在[3915,4000]Psi;\r\n \t c) 电压供电3.0±0.0717V时，压差力在[1414,1585]Psi。" },
                        { "压差传感器信号采集测试", "\t a) 电流供电4±0.2mA时，压力值在[0,3.4]Psid;\r\n \t b) 电流供电20±0.2mA时，压力值在[121.5,128.4]Psid;\r\n \t c) 电流供电10±0.2mA时，压力值在[43.44,50.31]Psid。" },
                        { "油量传感器信号采集测试", "\t a) 31-32/33-34针脚采集信号频率3200±32Hz，电压有效值6±1Vrms;\r\n \t b) 2/3号系统油量处于范围内。\r\n \t " },
                        { "离散量采集测试", "\t 针脚89/90采集为0，其余针脚采集结果为1。" },
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
                    "惰化模拟板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "电源阻抗测试", "\t 测量阻抗值大于500Ω则合格。" },
                        { "二次、三次电源测试", "\t a) 15V检测：15±1.5V；\r\n \t b) -15V检测：-15±1.5V；\r\n \t c) 5V检测：5±0.5V；\r\n \t d) 3.3V检测：3.3±0.33V。" },
                        { "超温切断模块电路测试", "\t a) PT500A电阻配置为（715.25±3.5）Ω：J31(T1_AWARN)输出高电平(3.3±0.33V)，J11(IIV +28VDC PWR IN_FB)开路(≤16V)，J12(IIV +28VDC PWR IN)开路(≤16V)。\r\n \t b) PT1000A电阻配置为（1411.6±7.1）Ω：J32(T2_AWARN)输出高电平(3.3±0.33V)，J13(TIV +28VDC PWR IN_FB)开路(≤16V)，J14(TIV +28VDC PWR IN)开路(≤16V)。" },
                        { "锁存模块电路测试", "\t a) PT500A=730Ω：J31输出为高电平(3.3±0.33V)；\r\n \t b) PT500A降低为500Ω：J31输出仍为高电平(3.3±0.33V)；\r\n \t c) J34供电3.3V后：J31输出为低电平(0±0.1V)；\r\n \t d) PT1000A=1500Ω：J32输出为高电平(3.3±0.33V)；\r\n \t e) PT1000A降低为1000Ω：J32输出仍为高电平(3.3±0.33V)；\r\n \t f) J35供电3.3V后：J32输出为低电平(0±0.1V)。" },
                    }
                },
                {
                    "惰化控制板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "控制板电源阻抗测试", "\t 控制板下电；测量J1、J2、J4、J5到J18（COM）、J70（EARTH）之间的阻抗，阻抗值大于500Ω则合格。" },
                        { "控制板二、三次电源测试", "\t a) 控制板-15V：读取电压值满足[-15.75V,-14.25V]为合格；\r\n \t b) 控制板+15V：读取电压值满足[14.25V,15.75V]为合格；\r\n \t c) 控制板5V：读取电压值满足[4.75V,5.25V]为合格；\r\n \t d) 控制板3.3V：读取电压值满足[3.135V,3.465V]为合格；\r\n \t e) 控制板1.5V：读取电压值满足[1.425V,1.575V]为合格。" },
                        { "控制板离散输入模块测试", "\t 控制板供电28V；将引脚J40-J45、J75-J83分别配置为GND和开路，通过通信读取采集结果；将引脚J84、J85分别配置为28V和开路，通过通信读取采集结果。采集结果应与配置状态一致。" },
                        { "离散输出模块测试", "\t 控制板供电28V；通过ARINC429通道0发送Label173/SDI1高/低指令；J11/J12/J13/J14/J17输出状态应分别为GND/开路；J21/J22输出状态应分别为28V/开路。" },
                        { "温度传感器信号采集", "\t 控制板供电28V；按表7-2配置PT500A/PT500B/PT1000A/PT1000B模拟电阻值，通过通信读取换算温度；上位机读取温度满足表7-3与表7-4为合格。" },
                        { "压力传感器信号采集", "\t 通过引脚J25、J26将“压力传感器”的模拟电压按表7-5进行设置，通过通讯读取的“压力”的数值；上位机读取的“压力”数据显示的值满足表7-6为合格。" },
                        { "氧气传感器信号采集", "\t 通过引脚J23、J24和引脚J59、J60将“氧气浓度传感器”、“氧气压力传感器”的模拟电流按表7-7进行设置，通过通讯读取“氧气浓度”、“氧气压力”的数值；上位机读取的“氧气浓度”、“氧气压力”显示的值满足表7-8为合格。" },
                        { "TCV电机驱动测试", "\t 控制板供电28V；试验台使用电阻6Ω（功率不小于150W）和电感12mH模拟负载；分别设置步进频率500Hz/1000Hz，设置正转/反转并给出电机使能信号；上位机读取TCV电机A相(J9/J10)、B相(J7/J8)电流值；A、B每相电流读数不持续为0则合格。" },
                    }
                },
                {
                    "空气单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "6.1电源对地阻抗检查", "\t 电源的对地阻抗应不小于200Ω。" },
                        { "8.1电源对地阻抗测试", "\t 电源的对地阻抗应不小于200Ω。" },
                        { "7.1功率板电源对地阻抗测试", "\t 电源的对地阻抗应不小于200Ω。" },
                        { "7.3.1.1.2 A控制通道功率板RAIA直流电机驱动模块供电电压测试", "\t a) 接入50±1Ω负载，发送测试指令后，输出电压绝对值在[17，32]V范围内为PASS；\r\n\t b) 接入12±1Ω负载，发送测试指令后，输出电压绝对值在[17，32]V范围内为PASS。" },
                        { "6.15.2.1A控制通道28V/OC型100mA离散输出通道1输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.15.2.2A控制通道28V/OC型100mA离散输出通道2输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.15.3.1A控制通道28V/OC型400mA离散输出通道1输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.15.3.2A控制通道28V/OC型400mA离散输出通道2输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.15.3.3A控制通道28V/OC型400mA离散输出通道3输出测试", "\t 控制器输出\"28V\"：离散输入接收\"28V\"信号，且离散输出电压在[25，28]V内；\r\n\t 控制器输出\"OC\"：离散输入接收\"OC\"信号。" },
                        { "6.13.1控制通道压力传感器采集测试", "\t a) 测试点1：输入1100±1mbar，采集压力在[1095.21,1104.79]mbar；\r\n\t b) 测试点2：输入1500±1mbar，采集压力在[1496.21,1504.79]mbar；\r\n\t c) 测试点3：输入2000±1mbar，采集压力在[1995.21,2004.79]mbar。" },
                        { "6.13.2 S安全通道压力传感器测试", "\t a) 测试点1：输入1100±1mbar，采集压力在[1082,1118]mbar；\r\n\t b) 测试点2：输入1500±1mbar，采集压力在[1482,1518]mbar；\r\n\t c) 测试点3：输入2000±1mbar，采集压力在[1982,2018]mbar。" },
                        { "6.5.1.1控制通道ARINC429发送通道1测试", "\t 上位机显示\"7F00AA55\"则检查通过。" },
                        { "8.5.1安全通道CAN发送测试", "\t 上位机显示\"01010101\"（对应CAN帧后4字节为01 01 01 01）则检查通过。" },
                        { "8.5.2安全通道CAN接收测试", "\t 上位机显示\"01010101\"则检查通过。" },
                        { "8.6.1 S安全通道WAITS1传感器测试", "\t a) 进入ATP后，按1/2/3挡依次接入电阻：351.65Ω、550.0Ω、693.53Ω；\r\n\t b) 发送测试指令（15 01 01 01 00 00 00 00）；\r\n\t c) 1挡温度范围[-77.05, -72.95]℃(10~50℃环境)或[-79.05, -70.95]℃；\r\n\t   2挡温度范围[23.63, 27.73]℃(10~50℃环境)或[21.63, 29.73]℃；\r\n\t   3挡温度范围[97.95, 102.05]℃(10~50℃环境)或[95.95, 104.05]℃。" },
                        { "8.6.2 S安全通道WAITS2传感器测试", "\t a) 进入ATP后，按1/2/3挡依次接入电阻：351.65Ω、550.0Ω、693.53Ω；\r\n\t b) 发送测试指令（15 01 02 01 00 00 00 00）；\r\n\t c) 1挡温度范围[-77.05, -72.95]℃(10~50℃环境)或[-79.05, -70.95]℃；\r\n\t   2挡温度范围[23.63, 27.73]℃(10~50℃环境)或[21.63, 29.73]℃；\r\n\t   3挡温度范围[97.95, 102.05]℃(10~50℃环境)或[95.95, 104.05]℃。" },
                        { "8.7.1S安全通道FWD_AVENTS1传感器测试", "\t a) 进入ATP后，按1/2/3挡依次接入电压：2.08±0.001V、3.00±0.001V、4.08±0.001V；\r\n\t b) 每挡发送S_FWDAVENTS_MEA01(15 02 01 01 00 00 00 00)，接收温度遥测(15 02 01 02 .. .. .. ..)；\r\n\t c) 1挡温度范围[-65.98, -64.02]℃，2挡[25.12, 28.88]℃，3挡[134.02, 137.98]℃。" },
                        { "8.7.2S安全通道FWD_AVENTS2传感器测试", "\t a) 进入ATP后，按1/2/3挡依次接入电压：2.08±0.001V、3.00±0.001V、4.08±0.001V；\r\n\t b) 每挡发送S_FWDAVENTS_MEA02(15 02 02 01 00 00 00 00)，接收温度遥测(15 02 02 02 .. .. .. ..)；\r\n\t c) 1挡温度范围[-65.98, -64.02]℃，2挡[25.12, 28.88]℃，3挡[134.02, 137.98]℃。" },
                        { "8.7.3S安全通道AFT_AVENTS传感器测试", "\t a) 测试J55、J56；\r\n\t b) 进入ATP后，按1/2/3挡依次接入电压：2.08±0.001V、3.00±0.001V、4.08±0.001V；\r\n\t c) 每挡发送S_AFTAVENTS_MEA(15 02 03 01 00 00 00 00)，接收温度遥测(15 02 03 02 .. .. .. ..)；\r\n\t d) 1挡温度范围[-65.98, -64.02]℃，2挡[25.12, 28.88]℃，3挡[134.02, 137.98]℃。" },
                        { "6.10.4控制通道WAIPSI2传感器测试", "\t a) 进入ATP后，按1/2/3挡依次接入电压：0.25V、5.00V、9.75V；\r\n\t b) 每挡发送压力测试指令(07 03 04 01 00 00 00 00)，接收压力遥测(07 03 04 02 .. .. .. ..)；\r\n\t c) 1挡压力范围[-3.7473, -1.5305]psia，2挡[46.3916, 48.6084]psia，3挡[96.5305, 98.7473]psia。" },
                    }
                }
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NotesByBoardType =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "液压单板",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "温度采集测试",
                            ""
                        },
                        {
                            "压差传感器信号采集测试",
                            ""
                        },
                        {
                            "离散量采集测试",
                            ""
                        },

                    }
                },
            };

        private string _testTaskName;
        private string _boardType;
        private string _parentChassisName;
        private string _pageKey;
        private object _rightPanelContent;
        private TestSequenceItem _selectedTestItem;
        private string _selectedTestCriteriaText;
        private string _selectedTestNotesText;

        /// <summary>右侧"操作步骤"面板标题</summary>
        public string TestNotesTitle => "注意事项";

        /// <summary>右侧"操作步骤"内容，随左侧选中测试项联动更新</summary>
        public string SelectedTestNotesText
        {
            get => _selectedTestNotesText;
            private set => SetProperty(ref _selectedTestNotesText, value);
        }

        private readonly HashSet<string> _navigationLockSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _isNavigationLocked;

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

        public bool IsBoardAccessible
        {
            get
            {
                var powered = _hydraulicPowerService?.PoweredBoardType;
                if (powered == null) return true;
                return string.Equals(powered, BoardType, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void OnPowerStateChanged(object sender, EventArgs e)
        {
            RaisePropertyChanged(nameof(IsBoardAccessible));
        }

        public BoardTestViewModel(IEventAggregator eventAggregator, ISingleBoardTestContextService singleBoardTestContext, IHydraulicPowerService hydraulicPowerService)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _singleBoardTestContext = singleBoardTestContext ?? throw new ArgumentNullException(nameof(singleBoardTestContext));
            _hydraulicPowerService = hydraulicPowerService;
            if (_hydraulicPowerService != null)
                _hydraulicPowerService.IsHydraulicPoweredChanged += OnPowerStateChanged;
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);

            _eventAggregator.GetEvent<NavigationLockChangedEvent>().Subscribe(OnNavigationLockChanged, ThreadOption.UIThread, keepSubscriberReferenceAlive: true);
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
                if (_isNavigationLocked && value != null && !ReferenceEquals(value, _selectedTestItem))
                {
                    try
                    {
                        MessageBox.Show("测试进行中，请先停止测试或等待测试结束后再切换界面。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                    }

                    RaisePropertyChanged(nameof(SelectedTestItem));
                    return;
                }

                if (_isStoppingCurrentTest)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        () => RaisePropertyChanged(nameof(SelectedTestItem)),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                var currentTestState = GetCurrentTestState();

                if (!ReferenceEquals(value, _selectedTestItem) && _selectedTestItem != null && currentTestState == CurrentTestState.Stopping)
                {
                    ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        () => RaisePropertyChanged(nameof(SelectedTestItem)),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                if (!ReferenceEquals(value, _selectedTestItem) && _selectedTestItem != null && currentTestState == CurrentTestState.Running)
                {
                    var result = ReMessageBox.Show("当前测试正在进行，是否停止测试并离开当前页面？", "提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                    if (result != System.Windows.MessageBoxResult.Yes)
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                            () => RaisePropertyChanged(nameof(SelectedTestItem)),
                            System.Windows.Threading.DispatcherPriority.Loaded);
                        return;
                    }

                    _ = StopCurrentTestAndContinueAsync(
                        onCompleted: () => SelectedTestItem = value,
                        onFailed: () => System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                            () => RaisePropertyChanged(nameof(SelectedTestItem)),
                            System.Windows.Threading.DispatcherPriority.Loaded));
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        () => RaisePropertyChanged(nameof(SelectedTestItem)),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                if (SetProperty(ref _selectedTestItem, value))
                {
                    if (value == null)
                    {
                        RightPanelContent = null;
                        SelectedTestCriteriaText = string.Empty;
                        SelectedTestNotesText = string.Empty;
                        return;
                    }

                    SelectedTestCriteriaText = ResolveCriteriaText(BoardType, value.Name);
                    SelectedTestNotesText = ResolveNotesText(BoardType, value.Name);

                    if (TryGetViewFactory(BoardType, value.Name, out var viewFactory))
                    {
                        RightPanelContent = viewFactory();
                        return;
                    }

                    RightPanelContent = new TextBlock { Text = value.Name, Margin = new System.Windows.Thickness(10) };
                }
            }
        }

        private void OnNavigationLockChanged(NavigationLockChangedEventArgs args)
        {
            var source = args?.Source;
            if (args?.IsLocked == true)
            {
                if (!string.IsNullOrWhiteSpace(source))
                    _navigationLockSources.Add(source);
                else
                    _navigationLockSources.Add("Unknown");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(source))
                    _navigationLockSources.Remove(source);
                else
                    _navigationLockSources.Clear();
            }

            _isNavigationLocked = _navigationLockSources.Count > 0;
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

            var instanceId = string.IsNullOrWhiteSpace(ParentChassisName) ? TestTaskName : $"{ParentChassisName}-{TestTaskName}";
            PageKey = string.IsNullOrWhiteSpace(instanceId) ? "BoardTest" : $"BoardTest_{instanceId}";

            _singleBoardTestContext.Update(ParentChassisName, TestTaskName, BoardType);

            LoadFixedTestItems(BoardType);
            RaisePropertyChanged(nameof(IsBoardAccessible));

            // ── FIX：直接赋值触发 setter，setter 内部会同时更新判据与操作步骤 ──
            // 原代码末尾多余的 SelectedTestNotesText = ResolveNotesText(BoardType, value.Name)
            // 已删除（value 在此方法中未定义，是编译错误的根源）
            SelectedTestItem = TestSequenceItems.FirstOrDefault();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            if (continuationCallback == null)
            {
                return;
            }

            var currentTestState = GetCurrentTestState();

            if (currentTestState == CurrentTestState.Stopping)
            {
                ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                continuationCallback(false);
                return;
            }

            if (currentTestState == CurrentTestState.Running)
            {
                var result = ReMessageBox.Show("当前测试正在进行，是否停止测试并离开当前页面？", "提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    continuationCallback(false);
                    return;
                }

                _ = StopCurrentTestAndContinueAsync(
                    onCompleted: () => continuationCallback(true),
                    onFailed: () => continuationCallback(false));
                return;
            }

            continuationCallback(true);
        }

        public bool CanClose()
        {
            var currentTestState = GetCurrentTestState();

            if (currentTestState == CurrentTestState.Stopping)
            {
                ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (currentTestState == CurrentTestState.Running)
            {
                var result = ReMessageBox.Show("存在正在测试的任务，是否停止测试？", "提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return false;
                }

                _ = StopCurrentTestAndContinueAsync(onCompleted: null, onFailed: null);
                return false;
            }

            return true;
        }

        private bool IsCurrentTestRunning()
        {
            return GetCurrentTestState() == CurrentTestState.Running;
        }

        private CurrentTestState GetCurrentTestState()
        {
            if (RightPanelContent is not System.Windows.FrameworkElement element)
            {
                return CurrentTestState.Idle;
            }

            var dc = element.DataContext;
            if (dc == null)
            {
                return CurrentTestState.Idle;
            }

            try
            {
                var type = dc.GetType();
                var pManual = type.GetProperty("IsManualTestRunning");
                var pAuto = type.GetProperty("IsAutoTestRunning");
                var pManualStopping = type.GetProperty("IsManualTestStopping");
                var pAutoStopping = type.GetProperty("IsAutoTestStopping");

                var manual = pManual?.PropertyType == typeof(bool) && (bool)(pManual.GetValue(dc) ?? false);
                var auto = pAuto?.PropertyType == typeof(bool) && (bool)(pAuto.GetValue(dc) ?? false);
                var manualStopping = pManualStopping?.PropertyType == typeof(bool) && (bool)(pManualStopping.GetValue(dc) ?? false);
                var autoStopping = pAutoStopping?.PropertyType == typeof(bool) && (bool)(pAutoStopping.GetValue(dc) ?? false);

                if (_isStoppingCurrentTest || manualStopping || autoStopping)
                {
                    return CurrentTestState.Stopping;
                }

                if (manual || auto)
                {
                    return CurrentTestState.Running;
                }

                return CurrentTestState.Idle;
            }
            catch
            {
                return CurrentTestState.Idle;
            }
        }

        public bool TryHandlePreviewSelection(TestSequenceItem nextItem)
        {
            if (nextItem == null)
            {
                return false;
            }

            if (ReferenceEquals(nextItem, _selectedTestItem))
            {
                return true;
            }

            if (_selectedTestItem == null)
            {
                return true;
            }

            if (_isStoppingCurrentTest)
            {
                ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            var currentTestState = GetCurrentTestState();
            if (currentTestState == CurrentTestState.Stopping)
            {
                ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (currentTestState == CurrentTestState.Running)
            {
                var result = ReMessageBox.Show("当前测试正在进行，是否停止测试并离开当前页面？", "提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return false;
                }

                _ = StopCurrentTestAndContinueAsync(
                    onCompleted: () => SelectedTestItem = nextItem,
                    onFailed: null);
                return false;
            }

            return true;
        }

        private async Task<bool> TryStopCurrentTestAsync()
        {
            if (RightPanelContent is not System.Windows.FrameworkElement element)
            {
                return true;
            }

            var dc = element.DataContext;
            if (dc == null)
            {
                return true;
            }

            try
            {
                var type = dc.GetType();
                var pManual = type.GetProperty("IsManualTestRunning");
                var pAuto = type.GetProperty("IsAutoTestRunning");

                var manual = pManual?.PropertyType == typeof(bool) && (bool)(pManual.GetValue(dc) ?? false);
                var auto = pAuto?.PropertyType == typeof(bool) && (bool)(pAuto.GetValue(dc) ?? false);

                if (manual)
                {
                    return await InvokeStopMethodAsync(dc, "StopManualTestAsync").ConfigureAwait(false);
                }

                if (auto)
                {
                    return await InvokeStopMethodAsync(dc, "StopAutoTestAsync").ConfigureAwait(false);
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private async Task StopCurrentTestAndContinueAsync(Action onCompleted, Action onFailed)
        {
            if (_isStoppingCurrentTest)
            {
                onFailed?.Invoke();
                return;
            }

            _isStoppingCurrentTest = true;
            try
            {
                var stopped = await TryStopCurrentTestAsync().ConfigureAwait(false);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _isStoppingCurrentTest = false;

                    if (stopped)
                    {
                        onCompleted?.Invoke();
                        return;
                    }

                    onFailed?.Invoke();
                });
            }
            catch
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _isStoppingCurrentTest = false;
                    onFailed?.Invoke();
                });
            }
            finally
            {
                _isStoppingCurrentTest = false;
            }
        }

        private static async Task<bool> InvokeStopMethodAsync(object target, string methodName)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            try
            {
                var method = target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method == null)
                {
                    return false;
                }

                var result = method.Invoke(target, null);
                if (result is System.Threading.Tasks.Task task)
                {
                    await task.ConfigureAwait(false);
                }

                return true;
            }
            catch
            {
                return false;
            }
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
                return string.Empty;

            if (CriteriaTextsByBoardType.TryGetValue(boardType, out var perBoard)
                && perBoard != null
                && perBoard.TryGetValue(testItemName, out var text))
            {
                return text ?? string.Empty;
            }

            return string.Empty;
        }

        private static string ResolveNotesText(string boardType, string testItemName)
        {
            if (string.IsNullOrWhiteSpace(boardType) || string.IsNullOrWhiteSpace(testItemName))
                return string.Empty;

            if (NotesByBoardType.TryGetValue(boardType, out var perBoard)
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
                TestSequenceItems.Add(new TestSequenceItem("6.1电源对地阻抗检查"));
                TestSequenceItems.Add(new TestSequenceItem("6.2.1A控制通道供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.1功率板电源对地阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.2.1A控制通道功率板供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.2.2B控制通道功率板供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.3.1.1.2 A控制通道功率板RAIA直流电机驱动模块供电电压测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.3.1.2 A控制通道功率板RATA直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.3.2.2 A控制通道功率板AVV直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("7.3.3.2 B控制通道PWM/FPGA速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.1控制通道PDTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.2控制通道MIXTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.3控制通道CAR_TS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.4控制通道CKPT_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.5控制通道CAB_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.6控制通道CAR_DTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.7控制通道BTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.8控制通道PTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.8.9控制通道CDTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.3 5V传感器供电电压测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.4控制通道光耦供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.1.1控制通道ARINC429发送通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.1.2A控制通道ARINC429发送通道2/B控制通道ARINC429接收通道5测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.1A控制通道ARINC接收通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.2A控制通道ARINC接收通道2测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.3A控制通道ARINC接收通道3测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.4A控制通道ARINC接收通道4测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.5A控制通道ARINC接收通道5测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.5.2.6A控制通道ARINC接收通道6测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.9.1A控制通道CKPT_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.9.2控制通道CAB_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.1控制通道BMPS压力传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.2A控制通道BPS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.3控制通道WAIPSI1传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.4控制通道WAIPSI2传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.5控制通道PDPS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.6A控制通道PIFS传感器测试"));
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
                TestSequenceItems.Add(new TestSequenceItem("8.1电源对地阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.1 S安全通道ARINC429发送通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.2 S安全通道ARINC429接收通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.3 S安全通道ARINC429接收通道2测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.10.1 S通道OFV/TRV直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.14.1控制通道GND/OC离散输入通道输入测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.1.1GND/OC型离散输出通道3输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.1.2GND/OC型100mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.2.1A控制通道28V/OC型100mA离散输出通道1输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.2.2A控制通道28V/OC型100mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.3.1A控制通道28V/OC型400mA离散输出通道1输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.3.2A控制通道28V/OC型400mA离散输出通道2输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.3.3A控制通道28V/OC型400mA离散输出通道3输出测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.6.1CAN发送测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.6.2CAN接收测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.5.1安全通道CAN发送测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.5.2安全通道CAN接收测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.6.1 S安全通道WAITS1传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.6.2 S安全通道WAITS2传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.7.1S安全通道FWD_AVENTS1传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.7.2S安全通道FWD_AVENTS2传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.7.3S安全通道AFT_AVENTS传感器测试"));
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

            if (string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("二次、三次电源测试"));
                TestSequenceItems.Add(new TestSequenceItem("超温切断模块电路测试"));
                TestSequenceItems.Add(new TestSequenceItem("锁存模块电路测试"));
                return;
            }

            if (string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("控制板电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制板二、三次电源测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制板离散输入模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("离散输出模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("温度传感器信号采集"));
                TestSequenceItems.Add(new TestSequenceItem("压力传感器信号采集"));
                TestSequenceItems.Add(new TestSequenceItem("氧气传感器信号采集"));
                TestSequenceItems.Add(new TestSequenceItem("TCV电机驱动测试"));
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
            var currentTestState = GetCurrentTestState();

            if (currentTestState == CurrentTestState.Stopping)
            {
                ReMessageBox.Show("请等待测试停止", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (currentTestState == CurrentTestState.Running)
            {
                ReMessageBox.Show("请先停止测试才能导航离开", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

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