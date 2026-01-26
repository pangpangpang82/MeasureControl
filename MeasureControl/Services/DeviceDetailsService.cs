using System;
using System.Collections.Generic;
using System.Linq;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services
{
    /// <summary>
    /// 设备详情服务实现 - 统一处理设备详情显示逻辑
    /// </summary>
    public class DeviceDetailsService : IDeviceDetailsService
    {
        /// <summary>
        /// 获取设备详情字典
        /// </summary>
        public Dictionary<string, string> GetDeviceDetails(DeviceBase device)
        {
            if (device == null)
                return new Dictionary<string, string>();

            var details = new Dictionary<string, string>();

            try
            {
                // 根据设备类型返回不同的详情
                switch (device.DeviceType)
                {
                    case "Chassis":
                        AddChassisDetails(device, details);
                        break;
                    case "Card":
                        AddCardDetails(device, details);
                        break;
                    case "Instrument":
                        AddInstrumentDetails(device, details);
                        break;
                    default:
                        AddDefaultDetails(device, details);
                        break;
                }
            }
            catch (Exception)
            {
                // 如果获取详情失败，返回基本信息
                details.Clear();
                details["名称"] = device.Name ?? "";
                details["类型"] = device.DeviceType ?? "";
            }

            return details;
        }

        /// <summary>
        /// 格式化设备信息标题
        /// </summary>
        public string FormatDeviceTitle(DeviceBase device)
        {
            if (device == null)
                return "设备信息";

            return $"设备信息 - {device.Name}";
        }

        /// <summary>
        /// 获取设备详情字段数组（用于绑定到 UI）
        /// </summary>
        public string[] GetDeviceFieldsArray(DeviceBase device, int maxFields = 6)
        {
            var fields = new string[maxFields];
            
            if (device == null)
            {
                for (int i = 0; i < maxFields; i++)
                    fields[i] = "";
                return fields;
            }

            var details = GetDeviceDetails(device);
            var detailsList = details.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();

            for (int i = 0; i < maxFields; i++)
            {
                fields[i] = i < detailsList.Count ? detailsList[i] : "";
            }

            return fields;
        }

        #region Private Helper Methods

        /// <summary>
        /// 添加机箱设备详情
        /// </summary>
        private void AddChassisDetails(DeviceBase device, Dictionary<string, string> details)
        {
            details["型号"] = device.Model ?? "";
            details["状态"] = device.Status ?? "";
            details["设备类型"] = "机箱";
            details["子设备数量"] = (device.Children?.Count ?? 0).ToString();
        }

        /// <summary>
        /// 添加板卡设备详情
        /// </summary>
        private void AddCardDetails(DeviceBase device, Dictionary<string, string> details)
        {
            details["型号"] = device.Model ?? "";
            details["父节点"] = device.ParentNode ?? "";
            details["连接方式"] = device.ConnectionMethod ?? "";
            details["状态"] = device.Status ?? "";
        }

        /// <summary>
        /// 添加仪器设备详情
        /// </summary>
        private void AddInstrumentDetails(DeviceBase device, Dictionary<string, string> details)
        {
            details["型号"] = device.Model ?? "";
            details["父节点"] = device.ParentNode ?? "";
            details["连接方式"] = device.ConnectionMethod ?? "";
        }

        /// <summary>
        /// 添加默认设备详情
        /// </summary>
        private void AddDefaultDetails(DeviceBase device, Dictionary<string, string> details)
        {
            details["名称"] = device.Name ?? "";
            details["类型"] = device.DeviceType ?? "";
        }

        #endregion
    }
}

