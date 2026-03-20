using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class FreqVoltageRangeOption
    {
        public int Index { get; set; }
        public string DisplayText { get; set; }
    }

    public class FreqVoltageRangeDialogViewModel : BindableBase
    {
        private ObservableCollection<FreqVoltageRangeOption> _options = new ObservableCollection<FreqVoltageRangeOption>();
        public ObservableCollection<FreqVoltageRangeOption> Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        private FreqVoltageRangeOption _selectedOption;
        public FreqVoltageRangeOption SelectedOption
        {
            get => _selectedOption;
            set => SetProperty(ref _selectedOption, value);
        }

        public FreqVoltageRangeDialogViewModel()
        {
            Options = new ObservableCollection<FreqVoltageRangeOption>
            {
                new FreqVoltageRangeOption { Index = 0, DisplayText = "200mV" },
                new FreqVoltageRangeOption { Index = 1, DisplayText = "2V" },
                new FreqVoltageRangeOption { Index = 2, DisplayText = "20V" },
                new FreqVoltageRangeOption { Index = 3, DisplayText = "200V" },
                new FreqVoltageRangeOption { Index = 4, DisplayText = "750V" }
            };

            SelectedOption = Options.FirstOrDefault(o => o.Index == 2);
        }

        public void Initialize(int defaultIndex)
        {
            SelectedOption = Options.FirstOrDefault(o => o.Index == defaultIndex) ?? Options.FirstOrDefault(o => o.Index == 2);
        }
    }
}
