using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MeasureControl.Events;
using MeasureControl.Views.SingleBoardTest.AirController;
using MeasureControl.Views.SingleBoardTest.HydraulicController;
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
@@
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
                        { "电源模块测试", () => new AirSimpleSequenceView("电源模块测试") },
                        { "5V传感器供电电压测试", () => new AirSimpleSequenceView("5V传感器供电电压测试") },
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
                        { "压力传感器信号采集测试", () => new HC_6_3() },
                        { "压差传感器信号采集测试", () => new HC_6_3() },
                        { "油量传感器信号采集测试", () => new HC_6_3() },
                        { "离散量采集测试", () => new HC_6_3() },
                        { "离散量输出测试", () => new HC_6_3() },
                    }
                },
                {
                    CommonBoardTypeKey,
                    new Dictionary<string, Func<UserControl>>(StringComparer.OrdinalIgnoreCase)
                    {
                    }
                },
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

        private void LoadFixedTestItems(string boardType)
        {
            TestSequenceItems.Clear();

            if (string.Equals(boardType, "空气单板", StringComparison.OrdinalIgnoreCase))
            {
                TestSequenceItems.Add(new TestSequenceItem("电源对地阻抗检查"));
                TestSequenceItems.Add(new TestSequenceItem("电源模块测试"));
                TestSequenceItems.Add(new TestSequenceItem("PT500型温度传感器测试"));
                TestSequenceItems.Add(new TestSequenceItem("5V传感器供电电压测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道光耦供电测试"));
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
                return;
            }

            if (string.Equals(boardType, "惰化单板", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
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
