using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.ViewModels.IcdConfig;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using DialogServiceAlias = MeasureControl.Services.DialogService;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// 项目管理ViewModel - 负责项目的创建、打开、保存等操作
    /// </summary>
    public class ProjectManagementViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly ProjectService _projectService;
        private readonly IProjectSaveStateService _projectSaveStateService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ProjectItem> _currentProject;
        private string _currentProjectPath;
        private bool _isProjectModified = false;
        private bool _isProjectMenuOpen;

        #endregion

        #region Public Properties

        /// <summary>
        /// 当前打开的项目
        /// </summary>
        public ObservableCollection<ProjectItem> CurrentProject
        {
            get => _currentProject;
            set
            {
                if (SetProperty(ref _currentProject, value))
                {
                    (SaveProjectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    (CloseProjectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(HasProject));
                }
            }
        }

        /// <summary>
        /// 是否有打开的项目
        /// </summary>
        public bool HasProject => CurrentProject?.Count > 0;

        /// <summary>
        /// 当前项目文件路径
        /// </summary>
        public string CurrentProjectFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_currentProjectPath))
                    return string.Empty;
                return _currentProjectPath;
            }
        }

        /// <summary>
        /// 项目菜单是否打开
        /// </summary>
        public bool IsProjectMenuOpen
        {
            get => _isProjectMenuOpen;
            set => SetProperty(ref _isProjectMenuOpen, value);
        }

        /// <summary>
        /// 项目是否已修改
        /// </summary>
        public bool IsProjectModified
        {
            get => _isProjectModified;
            private set => SetProperty(ref _isProjectModified, value);
        }

        /// <summary>
        /// 项目菜单项集合
        /// </summary>
        public ObservableCollection<MenuItemModel> ProjectMenuItems { get; set; }

        #endregion

        #region Commands

        public ICommand ShowProjectMenuCommand { get; private set; }
        public ICommand NewProjectCommand { get; private set; }
        public ICommand OpenProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand CloseProjectCommand { get; private set; }
        public ICommand CreateTestTaskCommand { get; private set; }
        public ICommand RenameTestTaskCommand { get; private set; }
        public ICommand DeleteTestTaskCommand { get; private set; }

        #endregion

        #region Constructor

        public ProjectManagementViewModel(
            ProjectService projectService,
            IProjectSaveStateService projectSaveStateService,
            IEventAggregator eventAggregator,
            IDialogService dialogService)
        {
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _projectSaveStateService = projectSaveStateService ?? throw new ArgumentNullException(nameof(projectSaveStateService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCollections();
            InitializeCommands();
            InitializeProjectMenu();
            SubscribeToEvents();
        }

        #endregion

        #region Private Methods

        private void InitializeCollections()
        {
            ProjectMenuItems = new ObservableCollection<MenuItemModel>();
        }

        private void InitializeCommands()
        {
            ShowProjectMenuCommand = new DelegateCommand(() => IsProjectMenuOpen = !IsProjectMenuOpen);
            NewProjectCommand = new DelegateCommand(OnNewProject);
            OpenProjectCommand = new DelegateCommand(OnOpenProject);
            SaveProjectCommand = new DelegateCommand(OnSaveProject, CanSaveProject);
            CloseProjectCommand = new DelegateCommand(OnCloseProject, CanCloseProject);
            CreateTestTaskCommand = new DelegateCommand<ProjectItem>(OnCreateTestTask);
            RenameTestTaskCommand = new DelegateCommand<RenameTestTaskEventArgs>(OnRenameTestTask);
            DeleteTestTaskCommand = new DelegateCommand<DeleteTestTaskEventArgs>(OnDeleteTestTask);
        }

        private void InitializeProjectMenu()
        {
            ProjectMenuItems.Clear();
            ProjectMenuItems.Add(new MenuItemModel { Header = "新建项目", Command = NewProjectCommand });
            ProjectMenuItems.Add(new MenuItemModel { Header = "打开项目", Command = OpenProjectCommand });
            ProjectMenuItems.Add(new MenuItemModel { Header = "保存项目", Command = SaveProjectCommand });
            ProjectMenuItems.Add(new MenuItemModel { Header = "关闭项目", Command = CloseProjectCommand });
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<ProjectModifiedEvent>().Subscribe(OnProjectModified);
            _eventAggregator.GetEvent<ProjectSavedEvent>().Subscribe(OnProjectSaved);
        }

        #endregion

        #region Command Implementations

        private void OnNewProject()
        {
            try
            {
                // 新建项目前清空所有相关缓存，避免旧数据残留
                ClearProjectCaches();

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "新建项目",
                    FileName = "新项目.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var projectName = Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
                    var newProject = _projectService.CreateNewProject(projectName, saveFileDialog.FileName);
                    
                    CurrentProject = new ObservableCollection<ProjectItem> { newProject };
                    _currentProjectPath = saveFileDialog.FileName;
                    CalibrationPathHelper.SetProjectPath(_currentProjectPath);
                    IsProjectModified = false;
                    
                    _eventAggregator.GetEvent<ProjectCreatedEvent>().Publish(newProject);
                    // 同时发布 ProjectOpenedEvent，确保订阅者同步刷新为新项目状态
                    _eventAggregator.GetEvent<ProjectOpenedEvent>().Publish(newProject);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectManagement] ===== OnNewProject ERROR =====");
                System.Diagnostics.Debug.WriteLine($"[ProjectManagement] Exception Type: {ex.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"[ProjectManagement] Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProjectManagement] StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectManagement] InnerException Type: {ex.InnerException.GetType().FullName}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectManagement] InnerException: {ex.InnerException.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectManagement] InnerException StackTrace: {ex.InnerException.StackTrace}");
                }
                _dialogService.ShowErrorDialog($"创建项目失败: {ex.Message}", "错误");
            }
        }

        private void OnOpenProject()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "打开项目"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    ClearProjectCaches();

                    var project = _projectService.LoadProject(openFileDialog.FileName);
                    CurrentProject = new ObservableCollection<ProjectItem> { project };
                    _currentProjectPath = openFileDialog.FileName;
                    CalibrationPathHelper.SetProjectPath(_currentProjectPath);
                    IsProjectModified = false;
                    
                    _eventAggregator.GetEvent<ProjectOpenedEvent>().Publish(project);
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"打开项目失败: {ex.Message}", "错误");
            }
        }

        private void OnSaveProject()
        {
            try
            {
                if (CurrentProject?.Count > 0 && !string.IsNullOrEmpty(_currentProjectPath))
                {
                    _projectService.SaveProject(CurrentProject.First(), _currentProjectPath);
                    IsProjectModified = false;
                    
                    _eventAggregator.GetEvent<ProjectSavedEvent>().Publish(CurrentProject.First());
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"保存项目失败: {ex.Message}", "错误");
            }
        }

        private bool CanSaveProject()
        {
            return HasProject && IsProjectModified;
        }

        private void OnCloseProject()
        {
            if (IsProjectModified)
            {
                var result = _dialogService.ShowConfirmationDialog("项目已修改，是否保存？", "确认");
                if (result == true)
                {
                    OnSaveProject();
                }
            }

            CurrentProject?.Clear();
            _currentProjectPath = string.Empty;
            CalibrationPathHelper.Reset();
            IsProjectModified = false;

            ClearProjectCaches();
            
            _eventAggregator.GetEvent<ProjectClosedEvent>().Publish();
        }

        private bool CanCloseProject()
        {
            return HasProject;
        }

        private void OnCreateTestTask(ProjectItem taskConfigNode)
        {
            if (taskConfigNode?.Type != "task_config") return;

            try
            {
                var dialogService = new DialogServiceAlias();
                var selected = dialogService.ShowCreateSingleBoardTestTaskDialog("创建测试任务");
                if (selected == null) return;

                var taskName = selected.TaskName?.Trim();
                if (string.IsNullOrWhiteSpace(taskName)) return;

                if (_projectService.IsTestTaskNameExists(taskConfigNode, taskName))
                {
                    _dialogService.ShowWarningDialog($"测试任务名称 '{taskName}' 已存在，请使用其他名称", "提示");
                    return;
                }

                var testTask = new ProjectItem
                {
                    Name = taskName,
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = selected.BoardType
                };

                taskConfigNode.Children.Add(testTask);
                IsProjectModified = true;
                _eventAggregator.GetEvent<TestTaskCreatedEvent>().Publish(testTask);
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"创建测试任务失败: {ex.Message}", "错误");
            }
        }

        private void OnRenameTestTask(RenameTestTaskEventArgs args)
        {
            if (args?.TestTask == null || string.IsNullOrWhiteSpace(args.NewName)) return;

            try
            {
                var taskConfigNode = FindTaskConfigNode(args.TestTask);
                if (taskConfigNode != null && _projectService.RenameTestTask(args.TestTask, args.NewName, taskConfigNode))
                {
                    IsProjectModified = true;
                    _eventAggregator.GetEvent<TestTaskRenamedEvent>().Publish(args);
                }
                else
                {
                    _dialogService.ShowWarningDialog("重命名失败，名称可能已存在", "警告");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"重命名测试任务失败: {ex.Message}", "错误");
            }
        }

        private void OnDeleteTestTask(DeleteTestTaskEventArgs args)
        {
            if (args?.TestTask == null) return;

            try
            {
                var taskConfigNode = FindTaskConfigNode(args.TestTask);
                if (taskConfigNode != null)
                {
                    _projectService.DeleteTestTask(taskConfigNode, args.TestTask);
                    IsProjectModified = true;
                    
                    _eventAggregator.GetEvent<TestTaskDeletedEvent>().Publish(args);
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"删除测试任务失败: {ex.Message}", "错误");
            }
        }

        #endregion

        private static void ClearProjectCaches()
        {
            ChannelConfigTabelViewModel.ClearAllChannelTabelItems();
            SignalConfigTabelViewModel.ClearAllSignalTabelItems();
            IcdConfigTabelViewModel.ClearAllIcdTabelItems();
            IcdMappingTabelViewModel.ClearAllIcdMappingItems();
        }

        #region Event Handlers

        private void OnProjectModified(ProjectModifiedEventArgs args)
        {
            IsProjectModified = true;
        }

        private void OnProjectSaved(ProjectItem project)
        {
            IsProjectModified = false;
        }

        #endregion

        #region Helper Methods

        private ProjectItem FindTaskConfigNode(ProjectItem testTask)
        {
            if (CurrentProject?.Count == 0) return null;

            var rootProject = CurrentProject.First();
            return FindTaskConfigNodeRecursive(rootProject, testTask);
        }

        private ProjectItem FindTaskConfigNodeRecursive(ProjectItem node, ProjectItem testTask)
        {
            if (node.Type == "task_config" && node.Children.Contains(testTask))
                return node;

            foreach (var child in node.Children)
            {
                var result = FindTaskConfigNodeRecursive(child, testTask);
                if (result != null) return result;
            }

            return null;
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 使用 ResourceCleanupHelper 清理集合
                ResourceCleanupHelper.CleanupCollection(_currentProject);
                ResourceCleanupHelper.CleanupCollection(ProjectMenuItems);
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
