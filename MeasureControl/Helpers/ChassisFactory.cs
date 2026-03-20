using System;
using System.Collections.Generic;
using System.Windows;
using MeasureControl.Models;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 机箱配置信息
    /// </summary>
    public class ChassisConfig
    {
        public string Manufacturer { get; set; } // 制造商
        public string Model { get; set; } // 型号
        public int SlotCount { get; set; } // 槽数
        public string DF1 { get; set; }
        public string DF2 { get; set; }

        //public string GenVersion { get; set; }
        //public double SystemBandwidth { get; set; }
        //public double SlotPowerCapacity { get; set; }
    }

    /// <summary>
    /// 机箱工厂类 - 用于创建不同类型的机箱模型
    /// </summary>
    public static class ChassisFactory
    {
        /// <summary>
        /// 机箱配置字典
        /// </summary>
        private static readonly Dictionary<string, ChassisConfig> ChassisConfigs = new Dictionary<string, ChassisConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["PXIe-2722G2"] = new ChassisConfig
            {
                Manufacturer = "简仪",
                Model = "PXIe-2722G2",
                SlotCount = 18,
                DF1 = "占位符1",
                DF2 = "占位符2",
                //GenVersion = "Gen2",
                //SystemBandwidth = 8.0,
                //SlotPowerCapacity = 58
            },
            ["PXIe-2519G2"] = new ChassisConfig
            {
                Manufacturer = "简仪",
                Model = "PXIe-2519G2",
                SlotCount = 9,
                DF1 = "占位符1",
                DF2 = "占位符2",
                //GenVersion = "Gen2",
                //SystemBandwidth = 8.0,
                //SlotPowerCapacity = 58
            }
        };

        /// <summary>
        /// 根据机箱型号创建对应的机箱模型
        /// </summary>
        public static ChassisModel CreateChassis(string chassisModel, string name, int row, int column)
        {
            // 规范化型号名称（移除空格、转大写）
            var normalizedModel = chassisModel.ToUpper().Replace(" ", "");

            // 尝试从配置字典中查找
            ChassisConfig config = null;
            foreach (var kvp in ChassisConfigs)
            {
                var configKey = kvp.Key.ToUpper().Replace(" ", "");
                if (normalizedModel.Contains(configKey) || normalizedModel.Contains(kvp.Value.Model.ToUpper().Replace(" ", "")))
                {
                    config = kvp.Value;
                    break;
                }
            }

            if (config != null)
            {
                // 使用配置创建机箱
                return new ChassisModel(
                    name, row, column,
                    config.Manufacturer,
                    config.Model,
                    config.SlotCount,
                    config.DF1,
                    config.DF2
                );
            }
            else
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return null;
        }

        /// <summary>
        /// 获取机箱型号的槽位数
        /// </summary>
        public static int GetSlotCount(string chassisModel)
        {
            var normalizedModel = chassisModel.ToUpper().Replace(" ", "");

            foreach (var kvp in ChassisConfigs)
            {
                var configKey = kvp.Key.ToUpper().Replace(" ", "");
                if (normalizedModel.Contains(configKey))
                {
                    return kvp.Value.SlotCount;
                }
            }

            return 18; 
        }

        /// <summary>
        /// 获取机箱型号的显示名称
        /// </summary>
        public static string GetDisplayName(string chassisModel)
        {
            var normalizedModel = chassisModel.ToUpper().Replace(" ", "");

            foreach (var kvp in ChassisConfigs)
            {
                var configKey = kvp.Key.ToUpper().Replace(" ", "");
                if (normalizedModel.Contains(configKey))
                {
                    var config = kvp.Value;
                    return $"{config.Manufacturer} {config.Model} ({config.SlotCount}槽)";
                }
            }

            return chassisModel;
        }

        /// <summary>
        /// 获取机箱配置信息
        /// </summary>
        public static ChassisConfig GetChassisConfig(string chassisModel)
        {
            var normalizedModel = chassisModel.ToUpper().Replace(" ", "");

            foreach (var kvp in ChassisConfigs)
            {
                var configKey = kvp.Key.ToUpper().Replace(" ", "");
                if (normalizedModel.Contains(configKey))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取所有支持的机箱型号列表
        /// </summary>
        public static List<string> GetSupportedChassisModels()
        {
            var models = new List<string>();
            foreach (var config in ChassisConfigs.Values)
            {
                models.Add(config.Model);
            }
            return models;
        }
    }
}
