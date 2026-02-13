using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MeasureControl.Events;
using MeasureControl.Views.SingleBoardTest.AirController;
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

        private static readonly IReadOnlyDictionary<string, Func<UserControl>> TestItemViewFactories =
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
                { "6.9.1A控制通道CKPT_VENTS传感器测试", () => new A_C_6_9_1_1View() },
                { "6.9.2控制通道CAB_VENTS传感器测试", () => new A_C_6_9_2_1View() },
                { "6.10.1控制通道BMPS压力传感器测试", () => new A_C_6_10_1_1View() },
                { "6.10.2A控制通道BPS传感器测试", () => new A_C_6_10_2_1View() },
                { "6.10.7控制通道RAIA_POS传感器测试", () => new A_C_6_10_7_1View() },
                { "6.11.1控制通道角度反馈传感器测试", () => new A_C_6_11_1_1View() },
                { "6.12.1控制通道选气楔传感器测试", () => new A_C_6_12_1_1View() },
                { "6.15.1.1 A控制通道功率板RAIA直流电机驱动模块速度控制测试", () => new A_C_6_15_1_1View() },
                { "6.15.2.1 A控制通道功率板AWV直流电机驱动模块速度控制测试", () => new A_C_6_15_2_1View() },
                { "8.3.2 S安全通道ARINC429接收通道1测试", () => new S_C_8_3_2View() },
                { "8.3.3 S安全通道ARINC429接收通道2测试", () => new S_C_8_3_3View() },
                { "电源模块测试", () => new AirSimpleSequenceView("电源模块测试") },
                { "5V传感器供电电压测试", () => new Pot5VSupplyTestView() },
                { "CAN发送测试", () => new CanCommTestView() },
                { "CAN接收测试", () => new CanReceiveTestView() },
                { "安全板CAN测试", () => new AirSimpleSequenceView("安全板CAN测试") },
                { "RS422通信测试", () => new RS422CommTabView() },
                { "电源阻抗测试", () => new PowerImpedanceTestView() },
                { "二次电源测试", () => new SecondaryPowerTestView() },
            };

        private string _testTaskName;
        private string _boardType;
        private string _parentChassisName;
        private string _pageKey;
        private object _rightPanelContent;
        private TestSequenceItem _selectedTestItem;

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
                        return;
                    }

                    if (TestItemViewFactories.TryGetValue(value.Name, out var viewFactory))
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
                TestSequenceItems.Add(new TestSequenceItem("6.9.1A控制通道CKPT_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.9.2控制通道CAB_VENTS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.1控制通道BMPS压力传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.2A控制通道BPS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.10.7控制通道RAIA_POS传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.11.1控制通道角度反馈传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.12.1控制通道选气楔传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.1.1 A控制通道功率板RAIA直流电机驱动模块速度控制测试"));
                TestSequenceItems.Add(new TestSequenceItem("6.15.2.1 A控制通道功率板AWV直流电机驱动模块速度控制测试"));
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
                return;
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
