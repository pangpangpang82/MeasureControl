using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class HydraulicAutoTestSelectionDialogViewModel : BindableBase
    {
        private string _validationMessage;

        public HydraulicAutoTestSelectionDialogViewModel()
        {
            ConfirmCommand = new DelegateCommand(() => { }, () => CanConfirm)
                .ObservesProperty(() => CanConfirm)
                .ObservesProperty(() => ValidationMessage);
            SelectAllCommand = new DelegateCommand(SelectAll);
            ClearAllCommand = new DelegateCommand(ClearAll);
        }

        public ObservableCollection<HydraulicAutoTestSelectionItem> Items { get; } = new ObservableCollection<HydraulicAutoTestSelectionItem>();

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand SelectAllCommand { get; }

        public DelegateCommand ClearAllCommand { get; }

        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        public bool CanConfirm => SelectedItems.Any() && string.IsNullOrWhiteSpace(ValidationMessage);

        public void Initialize(string[] names, string[] mandatoryNames = null)
        {
            Items.Clear();
            if (names == null)
            {
                ValidateSelection();
                return;
            }

            var mandatorySet = mandatoryNames != null
                ? new System.Collections.Generic.HashSet<string>(mandatoryNames, System.StringComparer.OrdinalIgnoreCase)
                : new System.Collections.Generic.HashSet<string>();

            foreach (var name in names)
            {
                var item = new HydraulicAutoTestSelectionItem(name, mandatorySet.Contains(name));
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(HydraulicAutoTestSelectionItem.IsSelected))
                    {
                        RaisePropertyChanged(nameof(CanConfirm));
                        ValidateSelection();
                    }
                };
                Items.Add(item);
            }

            ValidateSelection();
        }

        public string[] SelectedItems => Items.Where(x => x.IsSelected).Select(x => x.Name).ToArray();

        public void ValidateSelection()
        {
            ValidationMessage = SelectedItems.Length == 0 ? "请至少勾选一个测试项" : string.Empty;
            RaisePropertyChanged(nameof(CanConfirm));
        }

        private void SelectAll()
        {
            foreach (var item in Items)
            {
                item.IsSelected = true;
            }

            ValidateSelection();
        }

        private void ClearAll()
        {
            foreach (var item in Items)
            {
                if (!item.IsMandatory)
                    item.IsSelected = false;
            }

            ValidateSelection();
        }
    }

    public class HydraulicAutoTestSelectionItem : BindableBase
    {
        private bool _isSelected = true;

        public HydraulicAutoTestSelectionItem(string name, bool isMandatory = false)
        {
            Name = name;
            IsMandatory = isMandatory;
            if (isMandatory) _isSelected = true;
        }

        public string Name { get; }

        public bool IsMandatory { get; }

        public bool IsEditable => !IsMandatory;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (IsMandatory) return;
                SetProperty(ref _isSelected, value);
            }
        }
    }
}
