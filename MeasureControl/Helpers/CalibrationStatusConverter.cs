using System;
using System.Globalization;
using System.Windows.Data;

namespace MeasureControl.Helpers
{
    ///// <summary>
    ///// 通道类型显示转换器（AI→模拟输入，AO→模拟输出，DI→离散输入，DO→离散输出）
    ///// </summary>
    //public class ChannelTypeDisplayConverter : IValueConverter
    //{
    //    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        if (value is string channelType)
    //        {
    //            return channelType switch
    //            {
    //                "AI" => "模拟输入",
    //                "AO" => "模拟输出",
    //                "DI" => "离散输入",
    //                "DO" => "离散输出",
    //                _ => channelType
    //            };
    //        }
    //        return "";
    //    }
    //    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}


    /// <summary>
    /// 校准状态到背景色的转换器
    /// </summary>
    public class CalibrationStatusToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCalibrated)
            {
                return isCalibrated ? "#65E994" : "#F6F4B4";
            }
            return "#F6F4B4";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 校准状态到前景色的转换器
    /// </summary>
    public class CalibrationStatusToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCalibrated)
            {
                return isCalibrated ? "#17a34a" : "#DDC84B";
            }
            return "#DDC84B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 校准状态到文本的转换器
    /// </summary>
    public class CalibrationStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCalibrated)
            {
                return isCalibrated ? "已校准" : "未校准";
            }
            return "未校准";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 未校准时显示"-"的斜率/截距转换器
    /// </summary>
    public class CalibrationValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[1] is bool isCalibrated)
            {
                if (!isCalibrated)
                {
                    return "-";
                }
                
                if (values[0] is double doubleValue)
                {
                    return doubleValue.ToString("F4");
                }
            }
            return "-";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 未校准时显示"----"的时间转换器
    /// </summary>
    public class CalibrationTimeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[1] is bool isCalibrated)
            {
                if (!isCalibrated)
                {
                    return "----";
                }
                
                if (values[0] is DateTime dateTime && dateTime != DateTime.MinValue)
                {
                    return dateTime.ToString("yyyy-MM-dd HH:mm");
                }
            }
            return "----";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

