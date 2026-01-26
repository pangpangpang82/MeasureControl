using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 用于调用方法并传递参数的多值转换器
    /// </summary>
    public class MethodCallConverter : IMultiValueConverter
    {
        /// <summary>
        /// 转换值
        /// </summary>
        /// <param name="values">值数组，其中第一个元素是对象实例，第二个元素是方法名，后续元素是方法参数</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">参数</param>
        /// <param name="culture">区域性</param>
        /// <returns>转换后的结果</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return null;

            try
            {
                var viewModel = values[0];
                var methodName = values[1] as string;
                var topology = values[2] as string;

                if (viewModel == null || string.IsNullOrEmpty(methodName))
                    return null;

                // 获取方法信息
                var methodInfo = viewModel.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (methodInfo == null)
                    return null;

                // 调用方法并返回结果
                return methodInfo.Invoke(viewModel, new object[] { topology });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 转换回值
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="targetTypes">目标类型</param>
        /// <param name="parameter">参数</param>
        /// <param name="culture">区域性</param>
        /// <returns>转换后的结果</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}