using MeasureControl.Constants;
using MeasureControl.Models;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MeasureControl.Helpers
{
    #region Generic Converters

    public class AlternationIndexToOneBasedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int idx)
            {
                return (idx + 1).ToString(culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 通用箭头转换器：根据 bool 值返回展开/收起符号
    /// 参数格式："expanded=▼;collapsed=▶"（可省略，使用默认）
    /// </summary>
    public class GenericArrowConverter : IValueConverter
    {
        public string DefaultExpanded { get; set; } = "▼";
        public string DefaultCollapsed { get; set; } = "▶";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string expanded = DefaultExpanded;
            string collapsed = DefaultCollapsed;

            if (parameter is string param && !string.IsNullOrWhiteSpace(param))
            {
                // 解析形如 expanded=▼;collapsed=▶
                var parts = param.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim().ToLowerInvariant();
                        var val = kv[1];
                        if (key == "expanded") expanded = val;
                        else if (key == "collapsed") collapsed = val;
                    }
                }
            }

            bool isExpanded = value is bool b && b;
            return isExpanded ? expanded : collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 通用可见性转换器
    /// 参数："mode=bool|stringIsNullOrEmpty;invert=true|false"
    /// </summary>
    public class GenericVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string mode = "bool";
            bool invert = false;
            Visibility falseVisibility = Visibility.Collapsed;

            if (parameter is string param && !string.IsNullOrWhiteSpace(param))
            {
                var parts = param.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim().ToLowerInvariant();
                        var val = kv[1].Trim();
                        if (key == "mode") mode = val.ToLowerInvariant();
                        else if (key == "invert") invert = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                        else if (key == "falsevalue")
                        {
                            falseVisibility = string.Equals(val, "hidden", StringComparison.OrdinalIgnoreCase)
                                ? Visibility.Hidden
                                : Visibility.Collapsed;
                        }
                    }
                }
            }

            bool visible = false;
            if (mode == "bool")
            {
                visible = value is bool b && b;
            }
            else if (mode == "stringisnullorempty")
            {
                visible = string.IsNullOrWhiteSpace(value as string);
            }

            if (invert) visible = !visible;
            return visible ? Visibility.Visible : falseVisibility;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 仅为向后兼容 Bool 模式提供简单 ConvertBack
            if (value is Visibility vis)
            {
                return vis == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// 通用序号显示转换器：values = [isEmpty(bool), (int), name(string)]
    /// </summary>
    public class GenericIndexDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return string.Empty;

            bool isEmpty = values[0] is bool b && b;
            int index = values[1] is int i ? i : 0;
            string name = values[2]?.ToString();

            if (isEmpty || string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return index > 0 ? index.ToString() : string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StringEqualsIgnoreCaseMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return false;
            }

            var a = values[0]?.ToString();
            var b = values[1]?.ToString();

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ObjectEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return false;
            }

            var a = values[0];
            var b = values[1];

            if (a == null || b == null)
            {
                return false;
            }

            return ReferenceEquals(a, b) || a.Equals(b);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    #region Boolean Converters

    /// <summary>
    /// 反转布尔值转换器
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }

    public class BoolToYesNoConverter : IValueConverter
    {
        public string TrueText { get; set; } = "是";
        public string FalseText { get; set; } = "否";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bb && bb;
            return b ? TrueText : FalseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim();
            if (string.Equals(s, TrueText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(s, FalseText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// 反转布尔值到可见性转换器
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// 布尔值到颜色转换器
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public string TrueColor { get; set; } = "#ffffff";
        public string FalseColor { get; set; } = "#b8b8b8";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string selectedColor = FalseColor;

            if (value is bool isActive)
            {
                // 优先使用 ConverterParameter
                if (parameter is string colorPair)
                {
                    var colors = colorPair.Split('|');
                    if (colors.Length == 2)
                    {
                        var trueColor = colors[0].Trim();
                        var falseColor = colors[1].Trim();
                        selectedColor = isActive ? trueColor : falseColor;
                        return ToBrush(selectedColor);
                    }
                }

                selectedColor = isActive ? TrueColor : FalseColor;
            }

            return ToBrush(selectedColor);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static Brush ToBrush(string colorString)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent);
            }
        }
    }

    /// <summary>
    /// 导航按钮颜色转换器（考虑悬停状态）
    /// </summary>
    public class NavigationButtonColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#b8b8b8"));

            bool isActive = values[0] is bool active && active;
            bool isMouseOver = values[1] is bool mouseOver && mouseOver;

            if (isActive)
            {
                // 当前页面：始终显示白色，不受鼠标悬停影响
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
            }
            else
            {
                // 非当前页面：根据鼠标悬停状态显示不同颜色
                if (isMouseOver)
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c8d4e0"));
                }
                else
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#b8b8b8"));
                }
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值到展开图标转换器（委托到通用版）
    /// </summary>
    public class BoolToExpansionIconConverter : IValueConverter
    {
        private static readonly GenericArrowConverter _inner = new GenericArrowConverter { DefaultExpanded = "▼", DefaultCollapsed = "▶" };
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 强制使用既有默认：展开▼，收起▶
            return _inner.Convert(value, targetType, parameter ?? "expanded=▼;collapsed=▶", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值到可见性转换器（委托到通用版）
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        private static readonly GenericVisibilityConverter _inner = new GenericVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return _inner.Convert(value, targetType, parameter ?? "mode=bool;invert=false", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return _inner.ConvertBack(value, targetType, parameter ?? "mode=bool;invert=false", culture);
        }
    }

    public class DirectionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string dir = value?.ToString() ?? string.Empty;
            if (dir.IndexOf("Transmit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                dir.IndexOf("TX", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3498DB"));
            }

            if (dir.IndexOf("Receive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                dir.IndexOf("RX", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27AE60"));
            }

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DirectionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null)
            {
                return Visibility.Visible;
            }

            string dir = value?.ToString() ?? string.Empty;
            string expected = parameter.ToString() ?? string.Empty;

            bool matches = dir.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!matches)
            {
                if ((expected.Equals("Transmit", StringComparison.OrdinalIgnoreCase) || expected.Equals("TX", StringComparison.OrdinalIgnoreCase)) &&
                    (dir.IndexOf("Transmit", StringComparison.OrdinalIgnoreCase) >= 0 || dir.IndexOf("TX", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matches = true;
                }
                else if ((expected.Equals("Receive", StringComparison.OrdinalIgnoreCase) || expected.Equals("RX", StringComparison.OrdinalIgnoreCase)) &&
                         (dir.IndexOf("Receive", StringComparison.OrdinalIgnoreCase) >= 0 || dir.IndexOf("RX", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matches = true;
                }
            }

            return matches ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToGreenRedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 注意：Nullable<bool> 装箱后，HasValue=true 时会装箱成 bool；HasValue=false 时为 null
            if (value is bool b && b)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27AE60"));
            }

            if (value is bool)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
            }

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToStatusSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 注意：Nullable<bool> 装箱后，HasValue=true 时会装箱成 bool；HasValue=false 时为 null
            if (value is bool b && b)
            {
                return "✔";
            }

            if (value is bool)
            {
                return "✖";
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    /// <summary>
    /// 多值布尔与转换器：对多个 bool 值做 AND 运算
    /// 用于在 XAML 中将多个条件合并为一个 IsEnabled 等属性
    /// </summary>
    public class BooleanAndConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0) return false;
            foreach (var v in values)
            {
                if (!(v is bool b) || !b) return false;
            }
            return true;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Baud number <-> string converter.
    /// Supports plain numbers ("500000") or suffixed forms like "500k", "500K", "1M".
    /// Converts numeric values (uint/int) to plain decimal string.
    /// </summary>
    public class BaudNumberStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            try
            {
                if (value is uint ui) return ui.ToString(culture ?? CultureInfo.InvariantCulture);
                if (value is int i) return i.ToString(culture ?? CultureInfo.InvariantCulture);
                if (uint.TryParse(value.ToString(), out uint parsed)) return parsed.ToString(culture ?? CultureInfo.InvariantCulture);
                return value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            if (string.IsNullOrEmpty(s)) return 0u;
            // support suffixed forms
            try
            {
                if (s.EndsWith("k", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Number, culture ?? CultureInfo.InvariantCulture, out double v))
                        return (uint)(v * 1000);
                }
                else if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Number, culture ?? CultureInfo.InvariantCulture, out double v))
                        return (uint)(v * 1000000);
                }
                else
                {
                    if (uint.TryParse(s.Replace(" ", ""), NumberStyles.Integer, culture ?? CultureInfo.InvariantCulture, out uint v))
                        return v;
                }
            }
            catch { }
            // fallback 0
            return 0u;
        }
    }

    /// <summary>
    /// Integer equals converter: returns true when value == parameter (both parsed as int)
    /// </summary>
    public class IntEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return false;
            try
            {
                int param = 0;
                if (!int.TryParse(parameter.ToString(), out param))
                    return false;

                if (value == null) return false;
                // value may be string or numeric
                if (int.TryParse(value.ToString(), out int v))
                    return v == param;
                if (value is uint ui) return (int)ui == param;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }


    #region GridLength Converters

    /// <summary>
    /// 将 DisplayName 根据映射表转换为 GridLength（像素宽度）。
    /// - 支持 ConverterParameter 传入形如 "名称A=120;名称B=160" 的映射字符串；
    /// - 未匹配到时返回 DefaultWidth（像素）；
    /// - 忽略大小写并去除键两端空白；
    /// - 非法或负值宽度将回退到 DefaultWidth。
    /// </summary>
    public class DisplayNameToGridLengthConverter : IValueConverter
    {
        public double DefaultWidth { get; set; } = 100.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string displayName = value?.ToString() ?? string.Empty;
            string mapping = parameter?.ToString() ?? string.Empty;

            double width = TryGetWidthFromMapping(displayName, mapping, DefaultWidth);
            if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width))
            {
                width = DefaultWidth;
            }

            // 仅返回像素宽度，便于共享列等场景统一计算最大值
            return new GridLength(width, GridUnitType.Pixel);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static double TryGetWidthFromMapping(string key, string mapping, double fallback)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(mapping))
            {
                return fallback;
            }

            // 解析 "A=120;B=160" 形式
            var pairs = mapping.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;

                var mapKey = kv[0].Trim();
                var mapVal = kv[1].Trim();

                if (string.Equals(mapKey, key?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(mapVal, NumberStyles.Number, CultureInfo.InvariantCulture, out double w) && w > 0)
                    {
                        return w;
                    }
                    break;
                }
            }
            return fallback;
        }
    }

    /// <summary>
    /// 多值版本：values[0] = DisplayName，values[1] = IDictionary&lt;string,double&gt; 映射
    /// 允许在 ViewModel 中提供宽度映射，避免在 XAML 中使用固定字符串。
    /// </summary>
    public class DisplayNameAndMapToGridLengthConverter : IMultiValueConverter
    {
        public double DefaultWidth { get; set; } = 100.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string displayName = values != null && values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
            IDictionary<string, double> map = null;
            if (values != null && values.Length > 1)
            {
                map = values[1] as IDictionary<string, double>;
            }

            double width = DefaultWidth;
            if (!string.IsNullOrWhiteSpace(displayName) && map != null)
            {
                if (map.TryGetValue(displayName, out double mapped) && mapped > 0 && !double.IsNaN(mapped) && !double.IsInfinity(mapped))
                {
                    width = mapped;
                }
            }

            return new GridLength(width, GridUnitType.Pixel);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FieldSharedSizeGroupConverter : IMultiValueConverter
    {
        public string DefaultGroup { get; set; } = "FieldGroup_Default";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string displayName = values != null && values.Length > 0 ? values[0]?.ToString() : null;
            string name = values != null && values.Length > 1 ? values[1]?.ToString() : null;

            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return DefaultGroup;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    #region String Converters

    /// <summary>
    /// 空字符串到可见性转换器（输入工号隐藏）
    /// </summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        private static readonly GenericVisibilityConverter _inner = new GenericVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return _inner.Convert(value, targetType, "mode=stringIsNullOrEmpty;invert=false", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 字符串到可见性转换器（空字符串隐藏）
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        private static readonly GenericVisibilityConverter _inner = new GenericVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 非空显示：等价于 stringIsNullOrEmpty 取反
            return _inner.Convert(value, targetType, "mode=stringIsNullOrEmpty;invert=true", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 字符串等于指定参数时可见的转换器（用于在 XAML 中按文本切换视图）
    /// ConverterParameter 为要比较的字符串（大小写不敏感）
    /// </summary>
    public class StringEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string left = value?.ToString() ?? string.Empty;
            string right = parameter?.ToString() ?? string.Empty;
            bool equal = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return equal ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }


    /// <summary>
    /// 字符串到画刷转换器
    /// </summary>
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorString && !string.IsNullOrEmpty(colorString))
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
                }
                catch
                {
                    return Brushes.Gray; // 默认颜色
                }
            }
            return Brushes.Gray; // 默认颜色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 型号显示转换器
    /// 如果是"制造商 型号"格式，只显示"型号"部分
    /// 如果不是这类格式就显示全称
    /// </summary>
    public class ModelDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return string.Empty;

            string model = value.ToString();

            // 品牌列表集中维护在 DeviceConstants，便于统一管理
            var brands = DeviceConstants.Manufacturer.Brands;

            foreach (string brand in brands)
            {
                if (model.StartsWith(brand + " ") && model.Length > brand.Length + 1)
                {
                    // 只显示空格后面的部分
                    return model.Substring(brand.Length + 1);
                }
            }

            // 其他格式显示全称
            return model;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    #region Device Type Converters

    /// <summary>
    /// 设备背景颜色转换器：参数 "hover" 返回悬停色，否则返回白色
    /// </summary>
    public class DeviceTypeToBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush HoverBrush = new SolidColorBrush(Color.FromRgb(211, 211, 211));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (parameter as string)?.ToLower() == "hover" ? HoverBrush : WhiteBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 悬停背景转换器（向后兼容，委托给DeviceTypeToBackgroundConverter）
    /// </summary>
    public class DeviceTypeToHoverBackgroundConverter : IValueConverter
    {
        private static readonly DeviceTypeToBackgroundConverter _inner = new DeviceTypeToBackgroundConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(value, targetType, "hover", culture);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    #region Grid Position Converters

    /// <summary>
    /// 通用网格位置可见性转换器：支持单值与多值两种绑定方式
    /// Mode=strict（多值：集合,row,column）/ simple（单值：集合，param="row,column"）
    /// </summary>
    public class GenericGridPositionVisibilityConverter : IValueConverter, IMultiValueConverter
    {
        public string Mode { get; set; } = "strict";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Simple 模式：value = ObservableCollection<ChassisModel>, parameter = "row,column"
            try
            {
                var chassisList = value as ObservableCollection<ChassisModel>;
                if (chassisList == null) return Visibility.Collapsed;

                var positionStr = parameter as string;
                if (string.IsNullOrWhiteSpace(positionStr)) return Visibility.Collapsed;

                var positions = positionStr.Split(',');
                if (positions.Length != 2) return Visibility.Collapsed;

                if (!int.TryParse(positions[0].Trim(), out int row) || !int.TryParse(positions[1].Trim(), out int column))
                    return Visibility.Collapsed;

                if (chassisList.Count == 0) return Visibility.Collapsed;

                var chassis = chassisList.FirstOrDefault(c => c != null && c.GridRow == row && c.GridColumn == column);
                return chassis != null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Strict 模式：values = [ObservableCollection<ChassisModel>, row(int), column(int)]
            try
            {
                if (values == null || values.Length != 3) return Visibility.Collapsed;
                if (!(values[0] is ObservableCollection<ChassisModel> chassisList)) return Visibility.Collapsed;
                if (!(values[1] is int row) || !(values[2] is int column)) return Visibility.Collapsed;
                if (chassisList == null || chassisList.Count == 0) return Visibility.Collapsed;

                var chassis = chassisList.FirstOrDefault(c => c != null && c.GridRow == row && c.GridColumn == column);
                return chassis != null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 网格位置到可见性转换器（多值） - 委托到通用版（strict）
    /// </summary>
    public class GridPositionToVisibilityConverter : IMultiValueConverter
    {
        private static readonly GenericGridPositionVisibilityConverter _inner = new GenericGridPositionVisibilityConverter { Mode = "strict" };
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(values, targetType, parameter, culture);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 网格位置到机箱转换器
    /// </summary>
    public class GridPositionToChassisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ObservableCollection<ChassisModel> chassisList) ||
                !(parameter is string positionStr))
            {
                return null;
            }

            var positions = positionStr.Split(',');
            if (positions.Length != 2 ||
                !int.TryParse(positions[0], out int row) ||
                !int.TryParse(positions[1], out int column))
            {
                return null;
            }

            return chassisList?.FirstOrDefault(c => c.GridRow == row && c.GridColumn == column);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 网格位置到可见性转换器（简化版） - 委托到通用版（simple）
    /// </summary>
    public class GridPositionToVisibilitySimpleConverter : IValueConverter
    {
        private static readonly GenericGridPositionVisibilityConverter _inner = new GenericGridPositionVisibilityConverter { Mode = "simple" };
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(value, targetType, parameter, culture);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    #region Title Converters

    /// <summary>
    /// 设备或连接线标题转换器
    /// </summary>
    public class DeviceOrConnectionTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDeviceDetailsVisible)
            {
                return isDeviceDetailsVisible ? "设备" : "连接线";
            }
            return "设备";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    #region Tree View Converters

    public class TreeViewItemIsTopLevelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TreeViewItem item)
            {
                DependencyObject parent = item;
                while (parent != null)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                    if (parent is TreeView)
                        return true;
                    if (parent is TreeViewItem)
                        return false;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 项目树缩进转换器
    /// </summary>
    public class LevelToIndentConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is TreeViewItem item && double.TryParse(values[1]?.ToString(), out double indentSize))
            {
                int level = GetTreeViewItemLevel(item);
                return new Thickness(level * indentSize, 0, 0, 0);
            }
            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private int GetTreeViewItemLevel(TreeViewItem item)
        {
            int level = 0;
            DependencyObject parent = item;
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is TreeViewItem)
                    level++;
                else if (parent is TreeView)
                    break;
            }
            return level;
        }
    }

    #endregion

    #region Password Box Helper

    /// <summary>
    /// 密码框辅助类
    /// </summary>
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached(
                "Attach",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, OnAttachChanged));

        public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);
        public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

        private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PasswordBox passwordBox)
            {
                if ((bool)e.NewValue)
                {
                    passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
                }
                else
                {
                    passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
                }
            }
        }

        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PasswordBox passwordBox)
            {
                string newPassword = e.NewValue as string ?? string.Empty;
                if (passwordBox.Password != newPassword)
                {
                    passwordBox.Password = newPassword;
                }
            }
        }

        public static readonly DependencyProperty HasTextProperty =
            DependencyProperty.RegisterAttached(
                "HasText",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false));

        public static bool GetHasText(DependencyObject obj) => (bool)obj.GetValue(HasTextProperty);
        public static void SetHasText(DependencyObject obj, bool value) => obj.SetValue(HasTextProperty, value);

        private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                SetHasText(passwordBox, passwordBox.Password.Length > 0);
                SetBoundPassword(passwordBox, passwordBox.Password);
            }
        }
    }

    #endregion

    #region ObjectEqualityConverter

    /// <summary>
    /// 比较两个对象是否相等的转换器
    /// </summary>
    public class ObjectEqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 2)
                return false;

            return Equals(values[0], values[1]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    #region String Converters

    /// <summary>
    /// 字符串到Double转换器（用于高度等数值属性）
    /// </summary>
    public class StringToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && double.TryParse(str, out double result))
            {
                return result;
            }
            return 40.0; // 默认值
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
            return "40";
        }
    }

    /// <summary>
    /// 机箱类型到图片路径转换器
    /// </summary>
    public class ChassisTypeToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string chassisType)
            {
                // 规范化机箱类型名称
                var normalizedType = chassisType.ToUpper().Replace(" ", "");

                // 根据机箱类型返回对应的图片路径
                if (normalizedType.Contains("2722G2") || normalizedType.Contains("PXIE-2722G2"))
                {
                    return "/Resources/Hardware/PXI-2722.png";
                }
                else if (normalizedType.Contains("2519G2") || normalizedType.Contains("PXIE-2519G2"))
                {
                    return "/Resources/Hardware/PXI-2519.png";
                }
            }

            // 默认返回通用PXI图片
            return "/Resources/Hardware/PXI-2722.png";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 板卡名称显示转换器（优先显示CardName，如果为空则显示Model）
    /// </summary>
    public class CardNameDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return string.Empty;

            string cardName = values[0] as string;
            string model = values[1] as string;

            if (!string.IsNullOrWhiteSpace(cardName))
                return cardName;

            return model ?? string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 序号显示转换器 - 当IsEmpty为true或名称为空时，不显示序号
    /// 用于通道和信号等场景，values = [IsEmpty, Index, Name]
    /// </summary>
    public class IndexDisplayConverter : IMultiValueConverter
    {
        private static readonly GenericIndexDisplayConverter _inner = new GenericIndexDisplayConverter();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(values, targetType, parameter, culture);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // 向后兼容别名
    public class ChannelIndexDisplayConverter : IndexDisplayConverter { }
    public class SignalIndexDisplayConverter : IndexDisplayConverter { }

    /// <summary>
    /// 空值显示转换器 - 当IsEmpty为true时返回空字符串，否则按F4格式显示数值
    /// </summary>
    public class EmptyValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return string.Empty;

            bool isEmpty = values[0] is bool && (bool)values[0];
            double value = 0.0;

            // 尝试从values[1]获取double值
            if (values[1] is double d)
            {
                value = d;
            }
            else if (values[1] != null && double.TryParse(values[1].ToString(), out double parsedValue))
            {
                value = parsedValue;
            }

            // 如果 IsEmpty 为 true，返回空字符串
            if (isEmpty)
                return string.Empty;

            // 否则按 F4 格式显示值（保留4位小数）
            return value.ToString("F4", culture ?? CultureInfo.InvariantCulture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 值+单位转换器：将值和单位组合显示，格式为 "值 单位"
    /// 数字量(单位为0/1)时，值只显示0或1
    /// </summary>
    public class ValueWithUnitConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return string.Empty;

            bool isEmpty = values[0] is bool && (bool)values[0];
            double value = 0.0;
            string unit = values[2] as string ?? string.Empty;

            // 尝试从values[1]获取double值
            if (values[1] is double d)
                value = d;
            else if (values[1] != null && double.TryParse(values[1].ToString(), out double parsedValue))
                value = parsedValue;

            if (isEmpty)
                return string.Empty;

            // 数字量(单位为0/1)时，值只显示0或1
            if (unit == "0/1")
                return value >= 0.5 ? "1" : "0";

            // 模拟量：格式化值（保留4位小数）
            string formattedValue = value.ToString("F4", culture ?? CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(unit) ? formattedValue : $"{formattedValue} {unit}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 整数到16进制字符串转换器（用于MsgID显示）
    /// 将整数转换为两位16进制字符串，如0->"00", 1->"01", 255->"FF"
    /// </summary>
    public class IntToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "00";

            int intValue = 0;
            if (value is int i)
                intValue = i;
            else if (value is uint u)
                intValue = (int)u;
            else if (int.TryParse(value.ToString(), out int parsed))
                intValue = parsed;

            // 格式化为两位16进制，大写，不足两位前补0
            return intValue.ToString("X2");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is string str))
                return 0;

            // 尝试将16进制字符串转换回整数
            if (int.TryParse(str, System.Globalization.NumberStyles.HexNumber, culture ?? CultureInfo.InvariantCulture, out int result))
                return result;

            return 0;
        }
    }

    #endregion

    #region switch
    /// <summary>
    /// 布尔值到笔刷转换器
    /// </summary>
    public class BooleanToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.Green;
        public Brush FalseBrush { get; set; } = Brushes.Red;
        public Brush NullBrush { get; set; } = Brushes.Gray;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueBrush : FalseBrush;
            }
            return NullBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 连接次数到线宽转换器
    /// </summary>
    public class CountToStrokeThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                // 连接次数越多，线越粗（最小1，最大5）
                return Math.Min(Math.Max(count / 10.0, 1), 5);
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// 节点类型到颜色转换器
    /// </summary>
    public class NodeTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string nodeType)
            {
                return nodeType switch
                {
                    "Input" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),   // Blue
                    "Output" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),   // Red
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 连接次数到颜色转换器
    /// </summary>
    public class ConnectionCountToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                if (count == 0) return new SolidColorBrush(Colors.Gray);
                if (count < 5) return new SolidColorBrush(Color.FromRgb(144, 202, 249));   // Light Blue
                if (count < 10) return new SolidColorBrush(Color.FromRgb(33, 150, 243));   // Blue
                if (count < 20) return new SolidColorBrush(Color.FromRgb(13, 71, 161));    // Dark Blue
                if (count < 50) return new SolidColorBrush(Color.FromRgb(156, 39, 176));   // Purple
                return new SolidColorBrush(Color.FromRgb(74, 20, 140));                    // Dark Purple
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }




    /// <summary>
    /// 布尔值到文本转换器
    /// </summary>
    public class BooleanToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "在线" : "离线";
            }
            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 连接次数到宽度转换器
    /// </summary>
    public class CountToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && parameter is string maxWidthStr && double.TryParse(maxWidthStr, out double maxWidth))
            {
                // 假设最多显示100次
                double percentage = Math.Min(count / 100.0, 1.0);
                return maxWidth * percentage;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 居中节点转换器（用于将坐标转换为左上角坐标）
    /// </summary>
    public class CenterNodeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double center && parameter is string sizeStr && double.TryParse(sizeStr, out double size))
            {
                return center - size / 2;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 输入节点计数转换器
    /// </summary>
    public class InputNodesCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ObservableCollection<TopologyNodeInfo> nodes)
            {
                return nodes.Count(n => n.NodeType == "Input").ToString();
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 输出节点计数转换器
    /// </summary>
    public class OutputNodesCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ObservableCollection<TopologyNodeInfo> nodes)
            {
                return nodes.Count(n => n.NodeType == "Output").ToString();
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #region Matrix Topology Converters

        /// <summary>
        /// 节点延长线计算转换器
        /// </summary>
        public class NodeExtensionLineConverter : IMultiValueConverter
        {
            /// <summary>
            /// 计算延长线的坐标
            /// </summary>
            /// <param name="values">[0]: X坐标, [1]: Y坐标, [2]: 节点类型(Input/Output), [3]: 延长线长度(可选)</param>
            /// <param name="targetType">目标类型</param>
            /// <param name="parameter">可选参数（用于控制返回值的哪一项，如 StartX/StartY/EndX/EndY）</param>
            /// <param name="culture">文化信息</param>
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 3)
                    return 0.0;

                // 解析坐标
                double x = values[0] is double xValue ? xValue : 0.0;
                double y = values[1] is double yValue ? yValue : 0.0;

                // 节点类型
                string nodeType = values[2] as string ?? "Input";

                // 延长线长度（可选，默认30像素）
                double extensionLength = 30.0;
                if (values.Length > 3 && values[3] is double length)
                    extensionLength = length;

                string param = parameter as string;

                // 输入节点（通常在左侧）
                if (nodeType == "Input")
                {
                    return CalculateInputNodeExtension(x, y, extensionLength, param);
                }
                // 输出节点（通常在顶部）
                else if (nodeType == "Output")
                {
                    return CalculateOutputNodeExtension(x, y, extensionLength, param);
                }

                return 0.0;
            }

            /// <summary>
            /// 计算输入节点的延长线坐标（向左延伸）
            /// </summary>
            private double CalculateInputNodeExtension(double x, double y, double length, string param)
            {
                switch (param)
                {
                    case "StartX":
                        return x - length; // 向左延伸
                    case "StartY":
                        return y;
                    case "EndX":
                        return x;
                    case "EndY":
                        return y;
                    default:
                        return 0.0;
                }
            }

            /// <summary>
            /// 计算输出节点的延长线坐标（向上延伸）
            /// </summary>
            private double CalculateOutputNodeExtension(double x, double y, double length, string param)
            {
                switch (param)
                {
                    case "StartX":
                        return x;
                    case "StartY":
                        return y - length; // 向上延伸
                    case "EndX":
                        return x;
                    case "EndY":
                        return y;
                    default:
                        return 0.0;
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 节点位置偏移转换器
        /// </summary>
        public class NodeDisplayPositionConverter : IMultiValueConverter
        {
            /// <summary>
            /// 计算节点的显示位置（偏移后的位置）
            /// </summary>
            /// <param name="values">[0]: 原始X坐标, [1]: 原始Y坐标, [2]: 节点类型, [3]: 偏移量(可选)</param>
            /// <param name="targetType">目标类型</param>
            /// <param name="parameter">可选参数（"X" 或 "Y" 指示返回的坐标分量）</param>
            /// <param name="culture">文化信息</param>
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 2)
                    return 0.0;

                double x = values[0] is double xValue ? xValue : 0.0;
                double y = values[1] is double yValue ? yValue : 0.0;

                string param = parameter as string ?? "X";
                string nodeType = values.Length > 2 ? values[2] as string : "Input";

                double offset = 35.0; // 默认偏移量
                if (values.Length > 3 && values[3] is double customOffset)
                    offset = customOffset;

                // 输入节点向左偏移，输出节点向上偏移
                if (nodeType == "Input")
                {
                    return param == "X" ? x - offset : y;
                }
                else if (nodeType == "Output")
                {
                    return param == "X" ? x : y - offset;
                }

                return param == "X" ? x : y;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 节点颜色转换器
        /// </summary>
        public class NodeColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string nodeType)
                {
                    return nodeType switch
                    {
                        "Input" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),   // 蓝色 #2196F3
                        "Output" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // 红色 #F44336
                        _ => new SolidColorBrush(Colors.Gray)                          // 灰色
                    };
                }
                return new SolidColorBrush(Colors.Gray);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 节点高亮颜色转换器
        /// </summary>
        public class NodeHighlightColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string nodeType)
                {
                    return nodeType switch
                    {
                        "Input" => new SolidColorBrush(Color.FromRgb(21, 101, 192)),    // 深蓝色
                        "Output" => new SolidColorBrush(Color.FromRgb(198, 40, 40)),    // 深红色
                        _ => new SolidColorBrush(Color.FromRgb(255, 64, 129))           // 粉色 #FF4081
                    };
                }
                return new SolidColorBrush(Color.FromRgb(255, 64, 129));
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 交叉点状态到颜色转换器
        /// </summary>
        public class CrossPointStateToColorConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 2)
                    return new SolidColorBrush(Colors.White);

                bool isConnected = values[0] is bool && (bool)values[0];
                bool isHovered = values.Length > 1 && values[1] is bool && (bool)values[1];

                if (isHovered)
                {
                    // 悬停状态：半透明蓝色
                    return new SolidColorBrush(Color.FromArgb(128, 33, 150, 243));
                }
                else if (isConnected)
                {
                    // 已连接：绿色
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                else
                {
                    // 未连接：白色
                    return new SolidColorBrush(Colors.White);
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 交叉点状态到边框颜色转换器
        /// </summary>
        public class CrossPointStateToStrokeConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 2)
                    return new SolidColorBrush(Color.FromRgb(158, 158, 158));

                bool isConnected = values[0] is bool && (bool)values[0];
                bool isHovered = values.Length > 1 && values[1] is bool && (bool)values[1];

                if (isHovered)
                {
                    // 悬停状态：蓝色
                    return new SolidColorBrush(Color.FromRgb(33, 150, 243));
                }
                else if (isConnected)
                {
                    // 已连接：绿色
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                else
                {
                    // 未连接：灰色
                    return new SolidColorBrush(Color.FromRgb(158, 158, 158));
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 交叉点鼠标悬停时的大小转换器
        /// </summary>
        public class CrossPointHoverSizeConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 2)
                    return 8.0;

                double baseSize = 8.0;
                bool isHovered = values[0] is bool && (bool)values[0];

                if (isHovered)
                {
                    // 悬停时放大
                    return baseSize * 1.5;
                }

                return baseSize;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 节点工具提示文本转换器
        /// </summary>
        public class NodeToolTipConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 3)
                    return string.Empty;

                string nodeType = values[0] as string ?? "Unknown";
                string index = values[1]?.ToString() ?? "0";
                string name = values[2] as string ?? string.Empty;

                string typeText = nodeType == "Input" ? "输入节点" : "输出节点";

                if (!string.IsNullOrEmpty(name))
                {
                    return $"{typeText} #{index}: {name}";
                }
                else
                {
                    return $"{typeText} #{index}";
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 统计信息文本转换器
        /// </summary>
        public class MatrixStatisticsConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values == null || values.Length < 4)
                    return "矩阵信息未知";

                int inputCount = values[0] is int i ? i : 0;
                int outputCount = values[1] is int o ? o : 0;
                int crossPointCount = values[2] is int c ? c : 0;
                int activeCount = values[3] is int a ? a : 0;

                double connectionRatio = crossPointCount > 0 ? (double)activeCount / crossPointCount * 100 : 0;

                return $"输入×{inputCount}，输出×{outputCount}，交叉点×{crossPointCount}，活跃连接{activeCount}个 ({connectionRatio:F1}%)";
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

       


        #endregion

    public class NodeConnectionStateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return new SolidColorBrush(Colors.Transparent);

            // values[0]: NodeType (string)
            // values[1]: IsConnected (bool)
            string nodeType = values[0] as string;
            bool isConnected = values[1] is bool && (bool)values[1];

            if (!isConnected)
                return new SolidColorBrush(Colors.Transparent);

            // 根据节点类型返回不同的半透明颜色
            return nodeType switch
            {
                "Input" => new SolidColorBrush(Color.FromArgb(80, 33, 150, 243)),    // 浅蓝色，透明度80
                "Output" => new SolidColorBrush(Color.FromArgb(80, 244, 67, 54)),   // 浅红色，透明度80
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }





    public class NodeIdToNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string nodeId && !string.IsNullOrEmpty(nodeId))
            {
                // 从NodeId中提取数字部分（去除字母前缀）
                string numberPart = new string(nodeId.Where(char.IsDigit).ToArray());
                return numberPart;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 通道选择转换器：0 -> 通道B（备份）, 1 -> 通道A（主）
    /// 注意：这是1553B双冗余总线的概念（A是主通道，B是备份通道），不是物理通道0/1
    /// </summary>
    public class ChannelSelectConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int channel)
            {
                return channel == 0 ? "通道B（备份）" : "通道A（主）";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string channelString)
            {
                if (channelString.Contains("通道B") || channelString.Contains("备份")) return 0;
                if (channelString.Contains("通道A") || channelString.Contains("主")) return 1;
            }
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// RT使能按钮文本转换器：True -> "已使能", False -> "未使能"
    /// </summary>
    public class RTEnableButtonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
            {
                return isEnabled ? "已使能" : "未使能";
            }
            return "未使能";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return text == "已使能";
            }
            return false;
        }
    }

    /// <summary>
    /// RT使能按钮内容转换器：根据IsEnabled返回"已使能"或"未使能"
    /// </summary>
    public class RTEnableButtonContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
            {
                return isEnabled ? "已使能" : "未使能";
            }
            return "未使能";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// RT使能按钮样式转换器：根据IsEnabled返回不同的样式名称
    /// </summary>
    public class RTEnableButtonStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
            {
                return isEnabled ? "RTEnabledButtonStyle" : "RTDisabledButtonStyle";
            }
            return "RTDisabledButtonStyle";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string targetString)
            {
                return string.Equals(stringValue, targetString, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter is string targetString)
            {
                return targetString;
            }
            return Binding.DoNothing; // 返回DoNothing避免不匹配时设置null
        }
    }

}
