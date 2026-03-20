using System;
using System.Collections.ObjectModel;
using MeasureControl.Models;
using Prism.Commands;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 分页辅助类 - 提供通用分页计算逻辑
    /// </summary>
    public static class PaginationHelper
    {
        /// <summary>生成分页信息文本</summary>
        public static string GetPaginationInfo(int totalCount, int currentPage, int pageSize)
        {
            if (totalCount <= 0) return "显示0条到0条，共0条记录";
            int start = (currentPage - 1) * pageSize + 1;
            int end = Math.Min(currentPage * pageSize, totalCount);
            return $"显示{start}条到{end}条，共{totalCount}条记录";
        }

        /// <summary>计算总页数</summary>
        public static int GetTotalPages(int totalCount, int pageSize)
            => totalCount <= 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize);

        /// <summary>更新页码按钮集合</summary>
        public static void UpdatePageNumbers(ObservableCollection<PaginationButtonInfo> pageNumbers, 
            int totalPages, int currentPage, Action<int> goToPageAction)
        {
            pageNumbers.Clear();
            if (totalPages <= 0) return;

            int start, end;
            if (totalPages <= 3)
            {
                start = 1; end = totalPages;
            }
            else if (currentPage == 1)
            {
                start = 1; end = 3;
            }
            else if (currentPage == totalPages)
            {
                start = totalPages - 2; end = totalPages;
            }
            else
            {
                start = currentPage - 1; end = currentPage + 1;
            }

            for (int i = start; i <= end; i++)
            {
                int pageNum = i;
                pageNumbers.Add(new PaginationButtonInfo
                {
                    PageNumber = pageNum,
                    IsCurrentPage = pageNum == currentPage,
                    Command = new DelegateCommand(() => goToPageAction(pageNum))
                });
            }
        }
    }
}
