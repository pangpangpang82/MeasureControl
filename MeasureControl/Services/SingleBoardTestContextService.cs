using Prism.Mvvm;
using MeasureControl.Models;
using System.Linq;

namespace MeasureControl.Services
{
    public interface ISingleBoardTestContextService
    {
        string ChassisName { get; }
        string TestTaskName { get; }
        string BoardType { get; }

        void Update(string chassisName, string testTaskName, string boardType);

        /// <summary>
        /// 获取当前测试项对应的 ProjectItem 节点（用于持久化测试结果）
        /// </summary>
        ProjectItem GetCurrentTestItemNode(string testItemName);
    }

    public sealed class SingleBoardTestContextService : BindableBase, ISingleBoardTestContextService
    {
        private string _chassisName;
        private string _testTaskName;
        private string _boardType;
        private readonly ProjectService _projectService;

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

        public SingleBoardTestContextService(ProjectService projectService)
        {
            _projectService = projectService;
        }

        public void Update(string chassisName, string testTaskName, string boardType)
        {
            ChassisName = chassisName ?? string.Empty;
            TestTaskName = testTaskName ?? string.Empty;
            BoardType = boardType ?? string.Empty;
        }

        public ProjectItem GetCurrentTestItemNode(string testItemName)
        {
            if (_projectService?.CurrentProjectRoot == null || string.IsNullOrWhiteSpace(TestTaskName) || string.IsNullOrWhiteSpace(testItemName))
            {
                return null;
            }

            // 在项目树中查找当前测试任务节点
            var testTaskNode = FindTestTaskNode(_projectService.CurrentProjectRoot, TestTaskName);
            if (testTaskNode?.Children == null)
            {
                return null;
            }

            // 在测试任务节点下查找对应的测试项节点（通过 Name 匹配）
            return FindTestItemNodeRecursive(testTaskNode, testItemName);
        }

        private static ProjectItem FindTestTaskNode(ProjectItem root, string testTaskName)
        {
            if (root == null || root.Children == null)
            {
                return null;
            }

            foreach (var child in root.Children)
            {
                if (child.Type == "test_task" && child.Name == testTaskName)
                {
                    return child;
                }

                var found = FindTestTaskNode(child, testTaskName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ProjectItem FindTestItemNodeRecursive(ProjectItem parent, string testItemName)
        {
            if (parent == null)
            {
                return null;
            }

            if (parent.Name == testItemName)
            {
                return parent;
            }

            if (parent.Children == null)
            {
                return null;
            }

            foreach (var child in parent.Children)
            {
                var found = FindTestItemNodeRecursive(child, testItemName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
