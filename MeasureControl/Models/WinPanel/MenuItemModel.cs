using System.Windows.Input;

namespace MeasureControl.Models
{
    public class MenuItemModel
    {
        public string Header { get; set; }
        public ICommand Command { get; set; }
    }
}
