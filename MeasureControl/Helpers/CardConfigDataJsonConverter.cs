using System;
using System.Reflection;
using MeasureControl.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// CardConfigDataBase类型的JSON转换器，用于处理抽象类的序列化和反序列化
    /// </summary>
    public class CardConfigDataJsonConverter : JsonConverter<CardConfigDataBase>
    {
        public override void WriteJson(JsonWriter writer, CardConfigDataBase value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // 创建JSON对象，包含类型信息
            var jsonObject = new JObject { ["$type"] = value.GetType().FullName };

            // 使用反射自动序列化所有公共属性
            var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead)
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

        public override CardConfigDataBase ReadJson(JsonReader reader, Type objectType, CardConfigDataBase existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var jsonObject = JObject.Load(reader);
            
            // 获取类型信息
            var typeName = jsonObject["$type"]?.ToString();
            
            if (string.IsNullOrEmpty(typeName))
            {
                // 尝试根据 CardType 属性推断类型
                var cardType = jsonObject["CardType"]?.ToString();
                typeName = GetTypeNameFromCardType(cardType);
            }

            // 创建实例
            CardConfigDataBase config = CreateConfigByTypeName(typeName);
            if (config == null)
            {
                return null;
            }

            // 使用反射设置属性
            var properties = config.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanWrite || prop.Name == "$type")
                    continue;

                try
                {
                    var token = jsonObject[prop.Name];
                    if (token != null && token.Type != JTokenType.Null)
                    {
                        var propValue = token.ToObject(prop.PropertyType, serializer);
                        prop.SetValue(config, propValue);
                    }
                }
                catch
                {
                    // 忽略无法反序列化的属性
                }
            }

            return config;
        }

        private string GetTypeNameFromCardType(string cardType)
        {
            switch (cardType)
            {
                case "AnalogInput":
                    return typeof(AnalogInputCardConfig).FullName;
                case "AnalogOutput":
                    return typeof(AnalogOutputCardConfig).FullName;
                case "DigitalIO":
                    return typeof(DigitalIOCardConfig).FullName;
                case "ResistanceOutput":
                    return typeof(ResistanceOutputCardConfig).FullName;
                case "CAN":
                    return typeof(CanCardConfig).FullName;
                case "LVDS":
                    return typeof(LvdsCardConfig).FullName;
                default:
                    return null;
            }
        }

        private CardConfigDataBase CreateConfigByTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            try
            {
                // 尝试获取类型
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    // 在当前程序集中查找
                    type = Assembly.GetExecutingAssembly().GetType(typeName);
                }

                if (type != null && typeof(CardConfigDataBase).IsAssignableFrom(type))
                {
                    return (CardConfigDataBase)Activator.CreateInstance(type);
                }
            }
            catch
            {
                // 忽略创建失败
            }

            // 根据类型名称后缀推断
            if (typeName.Contains("AnalogInputCardConfig"))
                return new AnalogInputCardConfig();
            if (typeName.Contains("AnalogOutputCardConfig"))
                return new AnalogOutputCardConfig();
            if (typeName.Contains("DigitalIOCardConfig"))
                return new DigitalIOCardConfig();
            if (typeName.Contains("ResistanceOutputCardConfig"))
                return new ResistanceOutputCardConfig();
            if (typeName.Contains("CanCardConfig"))
                return new CanCardConfig();
            if (typeName.Contains("LvdsCardConfig"))
                return new LvdsCardConfig();

            return null;
        }
    }
}
