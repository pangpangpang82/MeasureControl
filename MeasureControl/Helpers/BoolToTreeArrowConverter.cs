using System;
using System.Globalization;
using System.Windows.Data;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 布尔值转换为树形箭头图标的转换器（委托到通用版）
    /// true（已展开）→ "◀"，false（未展开）→ "▼"
    /// </summary>
    public class BoolToTreeArrowConverter : IValueConverter
    {
        private static readonly GenericArrowConverter _inner = new GenericArrowConverter { DefaultExpanded = "◀", DefaultCollapsed = "▼" };
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return _inner.Convert(value, targetType, parameter ?? "expanded=◀;collapsed=▼", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
