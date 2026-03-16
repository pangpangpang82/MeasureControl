using System.Collections.Generic;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    public class NavigationButton : BindableBase
    {
        private bool _isActive;
        private string _tooltipPath;

        public string Name { get; set; }
        public string Tag { get; set; }
        public string PageName { get; set; }
        public string DisplayName { get; set; }
        public System.Windows.Input.ICommand Command { get; set; }

        /// <summary>
        /// 视图名称
        /// </summary>
        public string ViewName { get; set; }

        /// <summary>
        /// 导航参数
        /// </summary>
        public Dictionary<string, object> NavigationParams { get; set; }

        /// <summary>
        /// 浮悬提示路径
        /// </summary>
        public string TooltipPath
        {
            get => _tooltipPath;
            set => SetProperty(ref _tooltipPath, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }
}
