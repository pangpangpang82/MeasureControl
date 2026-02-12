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
                        { "控制通道光耦供电测试", () => new AC_6_4CommTabView() },
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
                        { "CAN发送测试", () => new CanCommTestView() },
                        { "CAN接收测试", () => new CanReceiveTestView() },
                        { "安全板CAN测试", () => new AirSimpleSequenceView("安全板CAN测试") },
                        { "RS422通信测试", () => new RS422CommTabView() },
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
                        { "电源阻抗测试", () => new PowerImpedanceTestView() },
                        { "二次电源测试", () => new SecondaryPowerTestView() },
                    }
                },
                {
                    CommonBoardTypeKey,
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                    }
                },
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
                        { "压力传感器信号采集测试", "\t a) 电压供电0.5±0.0717，压力值在[0,3.4]Psi;\r\n \t b) 电压供电7.17±0.0717V，压力值在[3915,4000]Psi;\r\n \t c) 电压供电3.0±0.0717V，压差力在[1414,1585]Psi。" },
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
                        { "电源阻抗测试", "\t a) 阻抗值大于500Ω；\r\n \t b) 阻抗值大于500Ω。" },
                        { "二次电源测试", "\t a) 5V隔离二次电源输出电压范围在[4.925，5.075]V；\r\n \t b) 15V隔离二次电源输出电压范围在[14.775，15.225]V；\r\n \t c) -15V隔离二次电源输出电压范围在[-14.775，-15.225]V。" },
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
                TestSequenceItems.Add(new TestSequenceItem("8.3.1 S安全通道ARINC429发送通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.2 S安全通道ARINC429接收通道1测试"));
                TestSequenceItems.Add(new TestSequenceItem("8.3.3 S安全通道ARINC429接收通道2测试"));
                TestSequenceItems.Add(new TestSequenceItem("ARINC429通讯测试"));
                TestSequenceItems.Add(new TestSequenceItem("CAN发送测试"));
                TestSequenceItems.Add(new TestSequenceItem("CAN接收测试"));
                TestSequenceItems.Add(new TestSequenceItem("安全板CAN测试"));
                TestSequenceItems.Add(new TestSequenceItem("RS422通信测试"));
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
                return;
            }

            if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源阻抗测试"));
                TestSequenceItems.Add(new TestSequenceItem("二次电源测试"));
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
