using System;
using System.Collections.ObjectModel;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class SelfInspectionDialogViewModel : BindableBase
    {
        private ObservableCollection<ProjectItem> _currentProject;

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

        public bool IsStartEnabled => SelectedChassis != null && string.IsNullOrEmpty(ValidationMessage);

        public void Initialize(ObservableCollection<ProjectItem> project)
        {
            _currentProject = project;
            LoadAvailableChassis();
            ValidateSelection();
        }

        public void ValidateSelection()
        {
            ValidationMessage = string.Empty;

            if (SelectedChassis == null)
            {
                ValidationMessage = "请选择机箱";
            }

            RaisePropertyChanged(nameof(IsStartEnabled));
        }

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

            if (AvailableChassis.Count > 0)
            {
                SelectedChassis = AvailableChassis[0];
            }
        }
    }
}
