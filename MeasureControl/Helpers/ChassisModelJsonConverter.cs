using System;
using MeasureControl.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// ChassisModel类型的JSON转换器，用于处理机箱的序列化和反序列化
    /// 支持向后兼容旧的三层继承结构数据
    /// </summary>
    public class ChassisModelJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ChassisModel) || objectType.Name.Contains("ChassisModel");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var chassis = value as ChassisModel;
            if (chassis == null)
            {
                writer.WriteNull();
                return;
            }

            // 创建JSON对象，包含类型信息
            var jsonObject = new JObject();
            
            // 保存类型信息（用于向后兼容）
            jsonObject["$type"] = "ChassisModel";

            // 序列化所有公共属性
            var properties = value.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead || prop.Name == "$type")
                    continue;

                try
                {
                    var propValue = prop.GetValue(value);
                    if (propValue != null)
                    {
                        jsonObject[prop.Name] = JToken.FromObject(propValue, serializer);
                    }
                }
                catch
                {
                    // 忽略无法序列化的属性
                }
            }

            jsonObject.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var jsonObject = JObject.Load(reader);

            // 获取基本信息
            var chassisName = jsonObject["Name"]?.ToString() ?? "新机箱";
            var gridRow = jsonObject["GridRow"]?.ToObject<int>() ?? 0;
            var gridColumn = jsonObject["GridColumn"]?.ToObject<int>() ?? 0;

            // 获取型号信息（用于判断使用哪个配置）
            var typeName = jsonObject["$type"]?.ToString();
            var chassisType = jsonObject["ChassisType"]?.ToString();
            var model = jsonObject["Model"]?.ToString();
            var slotCount = jsonObject["SlotCount"]?.ToObject<int>() ?? 18;

            ChassisModel chassis;

            // 尝试根据型号信息创建机箱
            string modelToCreate = null;

            // 优先使用 Model 字段
            if (!string.IsNullOrEmpty(model))
            {
                modelToCreate = model;
            }
            // 其次使用 ChassisType
            else if (!string.IsNullOrEmpty(chassisType) && chassisType != "Generic")
            {
                modelToCreate = chassisType;
            }
            // 最后根据旧的类型名称判断
            else if (!string.IsNullOrEmpty(typeName))
            {
                if (typeName.Contains("2722G2"))
                {
                    modelToCreate = "PXIe-2722G2";
                }
                else if (typeName.Contains("2519G2"))
                {
                    modelToCreate = "PXIe-2519G2";
                }
            }

            // 使用工厂方法创建机箱
            if (!string.IsNullOrEmpty(modelToCreate))
            {
                chassis = ChassisFactory.CreateChassis(modelToCreate, chassisName, gridRow, gridColumn);
            }
            else
            {
                // 未知型号，创建通用机箱
                chassis = new ChassisModel(chassisName, gridRow, gridColumn);
                chassis.SlotCount = slotCount; // 保留原有槽位数
            }

            // 反序列化所有属性
            try
            {
                serializer.Populate(jsonObject.CreateReader(), chassis);
            }
            catch (Exception)
            {
                // 如果自动反序列化失败，手动设置关键属性
                SetChassisProperties(chassis, jsonObject);
            }

            return chassis;
        }

        /// <summary>
        /// 手动设置机箱属性（当自动反序列化失败时使用）
        /// </summary>
        private void SetChassisProperties(ChassisModel chassis, JObject jsonObject)
        {
            if (chassis == null) return;

            try
            {
                // 基础属性
                if (jsonObject["Name"] != null) chassis.Name = jsonObject["Name"].ToString();
                if (jsonObject["GridRow"] != null) chassis.GridRow = jsonObject["GridRow"].ToObject<int>();
                if (jsonObject["GridColumn"] != null) chassis.GridColumn = jsonObject["GridColumn"].ToObject<int>();
                if (jsonObject["IsSelected"] != null) chassis.IsSelected = jsonObject["IsSelected"].ToObject<bool>();
                if (jsonObject["Id"] != null) chassis.Id = jsonObject["Id"].ToString();
                if (jsonObject["IpAddress"] != null) chassis.IpAddress = jsonObject["IpAddress"]?.ToString();
                if (jsonObject["ConnectionStatus"] != null) chassis.ConnectionStatus = jsonObject["ConnectionStatus"].ToString();
                if (jsonObject["SlotCount"] != null) chassis.SlotCount = jsonObject["SlotCount"].ToObject<int>();
                if (jsonObject["Manufacturer"] != null) chassis.Manufacturer = jsonObject["Manufacturer"]?.ToString();
                if (jsonObject["Model"] != null) chassis.Model = jsonObject["Model"]?.ToString();
                if (jsonObject["ChassisType"] != null) chassis.ChassisType = jsonObject["ChassisType"]?.ToString();

                // 新增属性
                if (jsonObject["DF1"] != null) chassis.DF1 = jsonObject["DF1"]?.ToString();
                if (jsonObject["DF2"] != null) chassis.DF2 = jsonObject["DF1"]?.ToString();
                // 忽略旧的物理属性（PowerSupply, CoolingType, Dimensions, Weight）用于向后兼容

                // 设备列表会通过DeviceBaseJsonConverter自动反序列化
                if (jsonObject["Devices"] is JArray && chassis.Devices != null)
                {
                    chassis.Devices.Clear();
                }
            }
            catch (Exception)
            {
                // 忽略错误
            }
        }
    }
}
