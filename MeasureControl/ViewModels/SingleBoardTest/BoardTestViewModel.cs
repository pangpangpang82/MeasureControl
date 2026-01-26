using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using MeasureControl.Events;
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

        public BoardTestViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
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
                    RightPanelContent = value == null ? null : new TextBlock { Text = value.Name, Margin = new System.Windows.Thickness(10) };
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
                TestSequenceItems.Add(new TestSequenceItem("5V传感器供电电压测试"));
                TestSequenceItems.Add(new TestSequenceItem("控制通道光耦供电测试"));
                TestSequenceItems.Add(new TestSequenceItem("ARINC429通讯测试"));
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
