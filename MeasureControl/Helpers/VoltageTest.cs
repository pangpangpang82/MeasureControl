using System;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 电压值格式化和验证测试类
    /// 用于测试电压输入功能的正确性
    /// </summary>
    public static class VoltageTest
    {
        /// <summary>
        /// 规范化电源电压文本
        /// - 如果输入整数，自动添加小数点和00
        /// - 验证范围：0.00 - 32.00
        /// - 小数部分截断到2位
        /// </summary>
        public static string NormalizePowerVoltageText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0.00";

            // 移除所有空格
            input = input.Trim();

            // 尝试解析为数字
            if (!double.TryParse(input, out double value))
                return "0.00"; // 返回默认值

            // 验证范围：0.00 - 32.00
            if (value < 0.00)
                value = 0.00;
            else if (value > 32.00)
                value = 32.00;

            // 格式化为2位小数
            string formatted = value.ToString("F2");

            return formatted;
        }

        /// <summary>
        /// 将电压转换为串口发送格式（去掉小数点，成为4位数字）
        /// 例如：5.00 -> "0500", 12.34 -> "1234"
        /// </summary>
        public static string GetPowerVoltageForSerial(string powerVoltageText)
        {
            if (!double.TryParse(powerVoltageText, out double value))
                return "0000";

            // 确保在有效范围内
            value = Math.Max(0.00, Math.Min(32.00, value));

            // 转换为4位数字字符串（乘以100后转为整数）
            int intValue = (int)Math.Round(value * 100);
            return intValue.ToString("D4"); // 确保4位，不足前面补0
        }

        /// <summary>
        /// 运行电压格式化测试
        /// </summary>
        public static void RunVoltageTests()
        {
            Console.WriteLine("Testing Voltage Formatting Functionality");
            Console.WriteLine("=======================================");

            // Test cases
            string[] testInputs = { "5", "5.5", "12.34", "32.1", "0", "-1", "33", "5.123", "abc", "", "  10  " };

            foreach (string input in testInputs)
            {
                string normalized = NormalizePowerVoltageText(input);
                string serialFormat = GetPowerVoltageForSerial(normalized);

                Console.WriteLine($"Input: '{input}' -> Normalized: '{normalized}' -> Serial: '{serialFormat}'");
            }

            Console.WriteLine("\nTest completed!");
        }
    }
}
