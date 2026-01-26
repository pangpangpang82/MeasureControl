using System.Windows.Input;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 分页按钮信息
    /// </summary>
    public class PaginationButtonInfo : BindableBase
    {
        private int _pageNumber;
        private bool _isCurrentPage;

        public int PageNumber
        {
            get => _pageNumber;
            set => SetProperty(ref _pageNumber, value);
        }

        public bool IsCurrentPage
        {
            get => _isCurrentPage;
            set => SetProperty(ref _isCurrentPage, value);
        }

        public ICommand Command { get; set; }
    }
}

