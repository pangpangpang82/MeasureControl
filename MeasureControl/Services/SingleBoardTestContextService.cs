using Prism.Mvvm;

namespace MeasureControl.Services
{
    public interface ISingleBoardTestContextService
    {
        string ChassisName { get; }
        string TestTaskName { get; }
        string BoardType { get; }

        void Update(string chassisName, string testTaskName, string boardType);
    }

    public sealed class SingleBoardTestContextService : BindableBase, ISingleBoardTestContextService
    {
        private string _chassisName;
        private string _testTaskName;
        private string _boardType;

        public string ChassisName
        {
            get => _chassisName;
            private set => SetProperty(ref _chassisName, value);
        }

        public string TestTaskName
        {
            get => _testTaskName;
            private set => SetProperty(ref _testTaskName, value);
        }

        public string BoardType
        {
            get => _boardType;
            private set => SetProperty(ref _boardType, value);
        }

        public void Update(string chassisName, string testTaskName, string boardType)
        {
            ChassisName = chassisName ?? string.Empty;
            TestTaskName = testTaskName ?? string.Empty;
            BoardType = boardType ?? string.Empty;
        }
    }
}
