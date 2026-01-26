using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 测试启动选择对话框的 ViewModel
    /// 仅选择机箱和测试任务，设备列表默认由机箱中已添加的设备决定。
    /// </summary>
    public class TestStartDialogViewModel : BindableBase, IDialogAware
    {
        private readonly IDialogService _dialogService;
        private ObservableCollection<ProjectItem> _currentProject;

        public string Title => "选择机箱与测试任务";

        #region Properties

        private ObservableCollection<ChassisModel> _availableChassis = new ObservableCollection<ChassisModel>();
        public ObservableCollection<ChassisModel> AvailableChassis
        {
            get => _availableChassis;
            set => SetProperty(ref _availableChassis, value);
        }

        private ChassisModel _selectedChassis;
        public ChassisModel SelectedChassis
        {
            get => _selectedChassis;
            set
            {
                if (SetProperty(ref _selectedChassis, value))
                {
                    LoadTestTasksForChassis(value);
                    ValidateSelection();
                }
            }
        }

        private ObservableCollection<ProjectItem> _availableTestTasks = new ObservableCollection<ProjectItem>();
        public ObservableCollection<ProjectItem> AvailableTestTasks
        {
            get => _availableTestTasks;
            set => SetProperty(ref _availableTestTasks, value);
        }

        private ProjectItem _selectedTestTask;
        public ProjectItem SelectedTestTask
        {
            get => _selectedTestTask;
            set
            {
                if (SetProperty(ref _selectedTestTask, value))
                {
                    ValidateSelection();
                }
            }
        }

        private string _validationMessage;
        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        public bool IsStartEnabled => SelectedChassis != null && SelectedTestTask != null && string.IsNullOrEmpty(ValidationMessage);

        #endregion

        #region Commands

        public DelegateCommand StartTestCommand { get; }
        public DelegateCommand CancelCommand { get; }

        #endregion

        #region Constructor

        public TestStartDialogViewModel(IDialogService dialogService = null)
        {
            _dialogService = dialogService;

            StartTestCommand = new DelegateCommand(OnStartTest)
                .ObservesCanExecute(() => IsStartEnabled)
                .ObservesProperty(() => SelectedChassis)
                .ObservesProperty(() => SelectedTestTask)
                .ObservesProperty(() => ValidationMessage);

            CancelCommand = new DelegateCommand(OnCancel);
        }

        #endregion

        #region IDialogAware Implementation

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.TryGetValue("CurrentProject", out ObservableCollection<ProjectItem> project))
            {
                Initialize(project);
            }
        }

        public event Action<IDialogResult> RequestClose;

        #endregion

        #region Public Methods

        public void Initialize(ObservableCollection<ProjectItem> project)
        {
            _currentProject = project;
            LoadAvailableChassis();
        }

        public void ValidateSelection()
        {
            ValidationMessage = string.Empty;

            if (SelectedChassis == null)
            {
                ValidationMessage = "请选择机箱";
            }
            else if (SelectedTestTask == null)
            {
                ValidationMessage = "请选择测试任务";
            }

            RaisePropertyChanged(nameof(IsStartEnabled));
        }

        #endregion

        #region Private Methods

        private void LoadAvailableChassis()
        {
            AvailableChassis.Clear();

            if (_currentProject?.Count > 0)
            {
                var rootNode = _currentProject[0];
                if (rootNode?.Children != null)
                {
                    foreach (var child in rootNode.Children)
                    {
                        if (child.Type == "PXIChassis")
                        {
                            // 从项目树中加载机箱列表，保持名称以便后续匹配
                            var chassis = new ChassisModel
                            {
                                Id = child.Name,
                                Name = child.Name,
                                Model = "PXI机箱"
                            };
                            AvailableChassis.Add(chassis);
                        }
                    }
                }
            }
        }

        private void LoadTestTasksForChassis(ChassisModel chassis)
        {
            AvailableTestTasks.Clear();

            if (chassis == null || _currentProject?.Count == 0)
                return;

            var rootNode = _currentProject[0];
            if (rootNode?.Children != null)
            {
                var chassisNode = rootNode.Children.FirstOrDefault(c => c.Name == chassis.Name && c.Type == "PXIChassis");
                if (chassisNode?.Children != null)
                {
                    var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == "task_config");
                    if (taskConfigNode?.Children != null)
                    {
                        foreach (var testTask in taskConfigNode.Children.Where(t => t.Type == "test_task"))
                        {
                            AvailableTestTasks.Add(testTask);
                        }
                    }
                }
            }
        }

        private void OnStartTest()
        {
            var result = new DialogResult(ButtonResult.OK, new DialogParameters
            {
                { "SelectedChassis", SelectedChassis },
                { "SelectedTestTask", SelectedTestTask }
            });

            RequestClose?.Invoke(result);
        }

        private void OnCancel()
        {
            var result = new DialogResult(ButtonResult.Cancel);
            RequestClose?.Invoke(result);
        }

        #endregion
    }
}
