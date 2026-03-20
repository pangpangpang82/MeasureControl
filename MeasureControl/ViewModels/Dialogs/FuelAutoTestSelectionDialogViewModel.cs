using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class FuelAutoTestSelectionDialogViewModel : BindableBase
    {
        private string _validationMessage;

        public FuelAutoTestSelectionDialogViewModel()
        {
            ConfirmCommand = new DelegateCommand(() => { }, () => CanConfirm)
                .ObservesProperty(() => CanConfirm)
                .ObservesProperty(() => ValidationMessage);
            SelectAllCommand = new DelegateCommand(SelectAll);
            ClearAllCommand = new DelegateCommand(ClearAll);
        }

        public ObservableCollection<FuelAutoTestSelectionItem> Items { get; } = new ObservableCollection<FuelAutoTestSelectionItem>();

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand SelectAllCommand { get; }

        public DelegateCommand ClearAllCommand { get; }

        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        public bool CanConfirm => SelectedItems.Any() && string.IsNullOrWhiteSpace(ValidationMessage);

        public void Initialize(string[] names)
        {
            Items.Clear();
            if (names == null)
            {
                ValidateSelection();
                return;
            }

            foreach (var name in names)
            {
                var item = new FuelAutoTestSelectionItem(name);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FuelAutoTestSelectionItem.IsSelected))
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
                item.IsSelected = false;
            }

            ValidateSelection();
        }
    }

    public class FuelAutoTestSelectionItem : BindableBase
    {
        private bool _isSelected = true;

        public FuelAutoTestSelectionItem(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
