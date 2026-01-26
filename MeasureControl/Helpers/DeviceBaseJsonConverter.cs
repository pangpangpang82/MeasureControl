using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// DeviceBase类型的JSON转换器，用于处理抽象类的序列化和反序列化
    /// 采用反射机制自动处理所有设备类型的序列化，无需手动添加每种设备类型的处理代码
    /// </summary>
    public class DeviceBaseJsonConverter : JsonConverter<DeviceBase>
    {
        public override void WriteJson(JsonWriter writer, DeviceBase value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // 创建JSON对象，包含类型信息
            var jsonObject = new JObject { ["$type"] = value.GetType().FullName };

            // 使用反射自动序列化所有公共属性（除了Children）
            var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                // 跳过Children，稍后单独处理；ChannelCalibrationRecord列表也序列化
                if (prop.Name == "Children" || !prop.CanRead)
                    continue;

                try
                {
                    var propValue = prop.GetValue(value);
                    if (propValue != null)
                    {
                        // 对于DeviceBase类型的属性（如子节点），使用递归序列化
                        if (typeof(DeviceBase).IsAssignableFrom(prop.PropertyType))
                        {
                            jsonObject[prop.Name] = JObject.FromObject(propValue, serializer);
                        }
                        // 对于CardConfigDataBase类型，使用serializer确保正确序列化
                        else if (typeof(CardConfigDataBase).IsAssignableFrom(prop.PropertyType))
                        {
                            jsonObject[prop.Name] = JToken.FromObject(propValue, serializer);
                        }
                        else
                        {
                            jsonObject[prop.Name] = JToken.FromObject(propValue, serializer);
                        }
                    }
                }
                catch
                {
                    // 忽略无法序列化的属性
                }
            }

            // 序列化子设备
            if (value.Children != null && value.Children.Count > 0)
            {
                var childrenArray = new JArray();
                foreach (var child in value.Children)
                {
                    childrenArray.Add(JObject.FromObject(child, serializer));
                }
                jsonObject["Children"] = childrenArray;
            }

            jsonObject.WriteTo(writer);
        }

        public override DeviceBase ReadJson(JsonReader reader, Type objectType, DeviceBase existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var jsonObject = JObject.Load(reader);
            
            // 获取类型信息和设备名称
            var typeName = jsonObject["$type"]?.ToString();
            var deviceName = jsonObject["Name"]?.ToString();
            var slotPosition = jsonObject["SlotPosition"]?.ToString() ?? "";
            var parentNode = jsonObject["ParentNode"]?.ToString();
            
            // 首先尝试通过反射创建设备实例（优先级最高）
            DeviceBase device = CreateDeviceByReflection(typeName);
            
            // 如果反射失败，则使用设备工厂创建
            if (device == null)
            {
                device = DeviceFactory.CreateDevice(typeName, deviceName, slotPosition);
            }

            // 如果创建的是GenericDevice，但ParentNode表明应该是特定类型的设备，则转换类型
            if (device is GenericDevice && !string.IsNullOrEmpty(parentNode))
            {
                device = ConvertToSpecificDevice(device, parentNode, deviceName, slotPosition);
            }

            // 反序列化属性
            try
            {
                serializer.Populate(jsonObject.CreateReader(), device);
            }
            catch (Exception)
            {
            }

            // 否则DeviceBase构造函数会生成新的Guid，导致依赖Id的业务数据（如标定 DeviceId/AIx）在重启后失效
            if (jsonObject["Id"] != null)
            {
                var idValue = jsonObject["Id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(idValue))
                {
                    device.Id = idValue;
                }
            }
            
            // 因为Populate可能无法正确处理这些属性，特别是当通过反射创建设备时
            // 先尝试从JSON中直接读取
            if (jsonObject["Name"] != null)
            {
                var nameValue = jsonObject["Name"].ToString();
                if (!string.IsNullOrEmpty(nameValue) && nameValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Name = nameValue;
                }
            }
            
            if (jsonObject["Manufacturer"] != null)
            {
                var manufacturerValue = jsonObject["Manufacturer"].ToString();
                if (!string.IsNullOrEmpty(manufacturerValue) && manufacturerValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Manufacturer = manufacturerValue;
                }
            }
            
            if (jsonObject["Model"] != null)
            {
                var modelValue = jsonObject["Model"].ToString();
                if (!string.IsNullOrEmpty(modelValue) && modelValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Model = modelValue;
                }
            }
            
            // 如果Name有值但Model为空，尝试从Name中解析
            if (!string.IsNullOrEmpty(device.Name) && device.Name != Constants.DeviceConstants.Default.NA
                && (string.IsNullOrEmpty(device.Model) || device.Model == Constants.DeviceConstants.Default.NA))
            {
                var parts = device.Name.Split(' ');
                if (parts.Length >= 2)
                {
                    device.Manufacturer = parts[0];
                    device.Model = string.Join(" ", parts.Skip(1));
                }
            }
            
            // 因为这些子节点是只读属性，Populate会静默跳过它们
            HandleSpecialChildNodes(device, jsonObject, serializer);

            // 处理子设备
            if (jsonObject["Children"] is JArray childrenArray)
            {
                device.Children.Clear();
                foreach (var childToken in childrenArray)
                {
                    if (childToken is JObject childObject)
                    {
                        var childDevice = ReadJson(childObject.CreateReader(), typeof(DeviceBase), null, false, serializer);
                        if (childDevice != null)
                        {
                            device.Children.Add(childDevice);
                        }
                    }
                }
            }
            
            // 这是因为Children数组可能包含了从JSON中加载的子节点，需要确保属性正确关联
            ReassignChildNodeProperties(device);
            
            // 如果CardName在JSON中存在但设备中为空，手动设置
            if (jsonObject["CardName"] != null && string.IsNullOrEmpty(device.CardName))
            {
                var cardNameValue = jsonObject["CardName"].ToString();
                if (!string.IsNullOrEmpty(cardNameValue))
                {
                    device.CardName = cardNameValue;
                }
            }

            // 确保这些基本属性能够正确从JSON中恢复
            if (jsonObject["Name"] != null)
            {
                var nameValue = jsonObject["Name"].ToString();
                if (!string.IsNullOrEmpty(nameValue) && nameValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Name = nameValue;
                }
            }

            if (jsonObject["Manufacturer"] != null)
            {
                var manufacturerValue = jsonObject["Manufacturer"].ToString();
                if (!string.IsNullOrEmpty(manufacturerValue) && manufacturerValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Manufacturer = manufacturerValue;
                }
            }

            // 处理Model属性：优先使用JSON中的值，如果为空则从Name中解析
            if (jsonObject["Model"] != null)
            {
                var modelValue = jsonObject["Model"].ToString();
                if (!string.IsNullOrEmpty(modelValue) && modelValue != Constants.DeviceConstants.Default.NA)
                {
                    device.Model = modelValue;
                }
            }
            
            // 如果Model仍然为空或N/A，但Name不为空，尝试从Name中解析
            if ((string.IsNullOrEmpty(device.Model) || device.Model == Constants.DeviceConstants.Default.NA) 
                && !string.IsNullOrEmpty(device.Name) && device.Name != Constants.DeviceConstants.Default.NA)
            {
                var parts = device.Name.Split(' ');
                if (parts.Length >= 2)
                {
                    device.Manufacturer = parts[0];
                    device.Model = string.Join(" ", parts.Skip(1));
                }
            }
            
            // 尝试从CardName推断设备信息（例如"矩阵开关1"可能对应某个设备）
            // 注意：这只是一个备选方案，理想情况下应该从JSON中恢复完整的设备信息
            if ((string.IsNullOrEmpty(device.Name) || device.Name == Constants.DeviceConstants.Default.NA)
                && (string.IsNullOrEmpty(device.Model) || device.Model == Constants.DeviceConstants.Default.NA)
                && !string.IsNullOrEmpty(device.CardName) && device.CardName != Constants.DeviceConstants.Default.NA)
            {
                // 如果CardName包含设备信息，可以尝试解析
                // 但这里我们只能设置Name为CardName，无法推断Manufacturer和Model
                // 因为CardName通常是用户自定义的名称，不包含设备型号信息
                device.Name = device.CardName;
            }

            // 因为 Populate 可能无法正确处理抽象类型
            if (jsonObject["CardConfigData"] is JObject cardConfigJson)
            {
                try
                {
                    var cardConfig = cardConfigJson.ToObject<CardConfigDataBase>(serializer);
                    if (cardConfig != null)
                    {
                        device.CardConfigData = cardConfig;
                    }
                }
                catch
                {
                    // 忽略反序列化失败
                }
            }

            return device;
        }

        /// <summary>
        /// 通过反射创建设备实例
        /// </summary>
        /// <param name="typeName">类型全名</param>
        /// <returns>设备实例，如果创建失败返回null</returns>
        private DeviceBase CreateDeviceByReflection(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            try
            {
                // 获取类型
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    // 尝试在当前程序集中查找
                    var assembly = Assembly.GetExecutingAssembly();
                    type = assembly.GetType(typeName);
                }

                // 如果仍然找不到类型，尝试在所有加载的程序集中查找
                if (type == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = assembly.GetType(typeName);
                        if (type != null)
                            break;
                    }
                }

                if (type == null || !typeof(DeviceBase).IsAssignableFrom(type))
                    return null;

                // 创建实例（使用无参构造函数）
                return (DeviceBase)Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 重新关联特定设备类型的子节点属性
        /// 在处理Children数组之后调用，确保属性正确关联
        /// </summary>
        private void ReassignChildNodeProperties(DeviceBase device)
        {
            if (device == null || device.Children == null || device.Children.Count == 0)
                return;

            try
            {
                // 处理AnalogAcquisitionDevice的AnalogInputNode属性
                if (device is AnalogAcquisitionDevice analogDevice)
                {
                    var analogInputNode = device.Children.OfType<AnalogInputNode>().FirstOrDefault();
                    if (analogInputNode != null)
                    {
                        analogDevice.AiNode = analogInputNode;
                    }
                }
                // 处理AnalogOutputDevice的AnalogOutputNode属性
                else if (device is AnalogOutputDevice analogOutputDevice)
                {
                    var analogOutputNode = device.Children.OfType<AnalogOutputNode>().FirstOrDefault();
                    if (analogOutputNode != null)
                    {
                        analogOutputDevice.AoNode = analogOutputNode;
                    }
                }
                // 处理ElectronicLoadDevice的ElectronicLoadChannelNode属性
                else if (device is ElectronicLoadDevice loadDevice)
                {
                    var channelNode = device.Children.OfType<ElectronicLoadChannelNode>().FirstOrDefault();
                    if (channelNode != null)
                    {
                        loadDevice.ElectronicLoadChannelNode = channelNode;
                    }
                }
                // 处理SwitchDevice的SwitchChannelNode属性
                else if (device is SwitchDevice switchDevice)
                {
                    var channelNode = device.Children.OfType<SwitchChannelNode>().FirstOrDefault();
                    if (channelNode != null)
                    {
                        switchDevice.SwitchChannelNode = channelNode;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略错误，避免影响反序列化流程
            }
        }

        /// <summary>
        /// 处理特殊的子节点（只读属性）
        /// </summary>
        private void HandleSpecialChildNodes(DeviceBase device, JObject jsonObject, JsonSerializer serializer)
        {
            if (device == null) return;

            try
            {
                // 处理AnalogAcquisitionDevice的AnalogInputNode
                if (device is AnalogAcquisitionDevice analogDevice)
                {
                    if (jsonObject["AnalogInputNode"] is JObject analogInputNodeObj)
                    {
                        var analogInputNode = analogInputNodeObj.ToObject<AnalogInputNode>(serializer);
                        if (analogInputNode != null)
                        {
                            // 找到Children中的AnalogInputNode并替换它
                            var existingNode = device.Children.OfType<AnalogInputNode>().FirstOrDefault();
                            if (existingNode != null)
                            {
                                device.Children.Remove(existingNode);
                            }
                            device.Children.Add(analogInputNode);
                            analogDevice.AiNode = analogInputNode;
                        }
                    }
                }
                // 处理AnalogOutputDevice的AnalogOutputNode
                else if (device is AnalogOutputDevice analogOutputDevice)
                {
                    if (jsonObject["AnalogOutputNode"] is JObject analogOutputNodeObj)
                    {
                        var analogOutputNode = analogOutputNodeObj.ToObject<AnalogOutputNode>(serializer);
                        if (analogOutputNode != null)
                        {
                            // 找到Children中的AnalogOutputNode并替换它
                            var existingNode = device.Children.OfType<AnalogOutputNode>().FirstOrDefault();
                            if (existingNode != null)
                            {
                                device.Children.Remove(existingNode);
                            }
                            device.Children.Add(analogOutputNode);
                            analogOutputDevice.AoNode = analogOutputNode;
                        }
                    }
                }
                // LvdsDevice现在使用LvdsInputNode和LvdsOutputNode，自动通过Children序列化
                // 处理ElectronicLoadDevice的ElectronicLoadChannelNode
                else if (device is ElectronicLoadDevice loadDevice)
                {
                    if (jsonObject["ElectronicLoadChannelNode"] is JObject channelNodeObj)
                    {
                        var channelNode = channelNodeObj.ToObject<ElectronicLoadChannelNode>(serializer);
                        if (channelNode != null)
                        {
                            var existingNode = device.Children.OfType<ElectronicLoadChannelNode>().FirstOrDefault();
                            if (existingNode != null)
                            {
                                device.Children.Remove(existingNode);
                            }
                            device.Children.Add(channelNode);
                            loadDevice.ElectronicLoadChannelNode = channelNode;
                        }
                    }
                }
                // 处理SwitchDevice的SwitchChannelNode
                else if (device is SwitchDevice switchDevice)
                {
                    if (jsonObject["SwitchChannelNode"] is JObject channelNodeObj)
                    {
                        var channelNode = channelNodeObj.ToObject<SwitchChannelNode>(serializer);
                        if (channelNode != null)
                        {
                            var existingNode = device.Children.OfType<SwitchChannelNode>().FirstOrDefault();
                            if (existingNode != null)
                            {
                                device.Children.Remove(existingNode);
                            }
                            device.Children.Add(channelNode);
                            switchDevice.SwitchChannelNode = channelNode;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 将GenericDevice转换为特定类型的设备
        /// </summary>
        private DeviceBase ConvertToSpecificDevice(DeviceBase genericDevice, string parentNode, string deviceName, string slotPosition)
        {
            if (string.IsNullOrEmpty(parentNode))
            {
                return genericDevice;
            }

            var lowerParentNode = parentNode.ToLower();

            // 根据ParentNode判断设备类型
            if (lowerParentNode.Contains("模拟量采集") || lowerParentNode.Contains("analog acquisition"))
            {
                var analogDevice = new AnalogAcquisitionDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, analogDevice);
                return analogDevice;
            }
            else if (lowerParentNode.Contains("模拟量输出") || lowerParentNode.Contains("analog output"))
            {
                var analogOutputDevice = new AnalogOutputDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, analogOutputDevice);
                return analogOutputDevice;
            }
            else if (lowerParentNode.Contains("数字万用表") || lowerParentNode.Contains("dmm"))
            {
                var dmmDevice = new DmmDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, dmmDevice);
                return dmmDevice;
            }
            else if (lowerParentNode.Contains("开关") || lowerParentNode.Contains("switch") || lowerParentNode.Contains("矩阵"))
            {
                var switchDevice = new SwitchDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, switchDevice);
                return switchDevice;
            }
            else if (lowerParentNode.Contains("程控电源") || lowerParentNode.Contains("power supply"))
            {
                var powerDevice = new PowerSupplyDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, powerDevice);
                return powerDevice;
            }
            else if (lowerParentNode.Contains("示波器") || lowerParentNode.Contains("oscilloscope"))
            {
                var scopeDevice = new OscilloscopeDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, scopeDevice);
                return scopeDevice;
            }
            else if (lowerParentNode.Contains("电子负载") || lowerParentNode.Contains("electronic load"))
            {
                var loadDevice = new ElectronicLoadDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, loadDevice);
                return loadDevice;
            }
            else if (lowerParentNode.Contains("lvds"))
            {
                var lvdsDevice = new LvdsDevice(deviceName, slotPosition);
                CopyBasicProperties(genericDevice, lvdsDevice);
                return lvdsDevice;
            }

            return genericDevice;
        }

        /// <summary>
        /// 复制基本属性
        /// </summary>
        private void CopyBasicProperties(DeviceBase source, DeviceBase target)
        {
            target.Name = source.Name;
            target.Manufacturer = source.Manufacturer;
            // 因为target可能已经从构造函数中设置了正确的Model（通过ParseDeviceName）
            if (!string.IsNullOrEmpty(source.Model) && source.Model != Constants.DeviceConstants.Default.NA)
            {
                target.Model = source.Model;
            }
            target.SlotPosition = source.SlotPosition;
            target.Status = source.Status;
            target.Description = source.Description;
            target.DeviceType = source.DeviceType;
            target.Id = source.Id;
            target.IsSelected = source.IsSelected;
            target.IsExpanded = source.IsExpanded;
            target.ConnectionMethod = source.ConnectionMethod;
            target.ParentNode = source.ParentNode;
            target.Details = source.Details;
            target.CardName = source.CardName;
        }

        /// <summary>
        /// 手动设置设备属性（当自动反序列化失败时使用）
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="jsonObject">JSON对象</param>
        private void SetDeviceProperties(DeviceBase device, JObject jsonObject)
        {
            if (device == null) return;

            try
            {
                if (jsonObject["Name"] != null) device.Name = jsonObject["Name"].ToString();
                if (jsonObject["Manufacturer"] != null) device.Manufacturer = jsonObject["Manufacturer"].ToString();
                if (jsonObject["Model"] != null) device.Model = jsonObject["Model"].ToString();
                if (jsonObject["SlotPosition"] != null) device.SlotPosition = jsonObject["SlotPosition"].ToString();
                if (jsonObject["Status"] != null) device.Status = jsonObject["Status"].ToString();
                if (jsonObject["Description"] != null) device.Description = jsonObject["Description"].ToString();
                if (jsonObject["DeviceType"] != null) device.DeviceType = jsonObject["DeviceType"].ToString();
                if (jsonObject["Id"] != null) device.Id = jsonObject["Id"].ToString();
                if (jsonObject["IsSelected"] != null) device.IsSelected = jsonObject["IsSelected"].ToObject<bool>();
                if (jsonObject["IsExpanded"] != null) device.IsExpanded = jsonObject["IsExpanded"].ToObject<bool>();
                if (jsonObject["ConnectionMethod"] != null) device.ConnectionMethod = jsonObject["ConnectionMethod"].ToString();
                if (jsonObject["ParentNode"] != null) device.ParentNode = jsonObject["ParentNode"].ToString();
                if (jsonObject["Details"] != null) device.Details = jsonObject["Details"].ToString();
                if (jsonObject["CardName"] != null) device.CardName = jsonObject["CardName"].ToString();
                if (jsonObject["CalibrationRecords"] != null)
                {
                    device.CalibrationRecords = jsonObject["CalibrationRecords"].ToObject<List<Models.ChannelCalibrationRecord>>();
                }
                
                // 处理特定设备类型的属性
                if (device.GetType().Name == "ChassisDevice")
                {
                    var chassisDevice = device as dynamic;
                    if (jsonObject["SlotCount"] != null) chassisDevice.SlotCount = jsonObject["SlotCount"].ToObject<int>();
                    if (jsonObject["ChassisModel"] != null) chassisDevice.ChassisModel = jsonObject["ChassisModel"].ToString();
                }
                else if (device.GetType().Name == "PowerSupplyDevice")
                {
                    var powerSupplyDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) powerSupplyDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["MaxVoltage"] != null) powerSupplyDevice.MaxVoltage = jsonObject["MaxVoltage"].ToObject<double>();
                    if (jsonObject["MaxCurrent"] != null) powerSupplyDevice.MaxCurrent = jsonObject["MaxCurrent"].ToObject<double>();
                    if (jsonObject["PowerRating"] != null) powerSupplyDevice.PowerRating = jsonObject["PowerRating"].ToObject<double>();
                }
                else if (device.GetType().Name == "ElectronicLoadDevice")
                {
                    var electronicLoadDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) electronicLoadDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["MaxVoltage"] != null) electronicLoadDevice.MaxVoltage = jsonObject["MaxVoltage"].ToObject<double>();
                    if (jsonObject["MaxCurrent"] != null) electronicLoadDevice.MaxCurrent = jsonObject["MaxCurrent"].ToObject<double>();
                    if (jsonObject["MaxPower"] != null) electronicLoadDevice.MaxPower = jsonObject["MaxPower"].ToObject<double>();
                    
                    // 反序列化ElectronicLoadChannelNode子节点
                    if (jsonObject["ElectronicLoadChannelNode"] is JObject electronicLoadChannelNodeObj)
                    {
                        electronicLoadDevice.ElectronicLoadChannelNode = electronicLoadChannelNodeObj.ToObject<ElectronicLoadChannelNode>();
                    }
                }
                else if (device.GetType().Name == "OscilloscopeDevice")
                {
                    var oscilloscopeDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) oscilloscopeDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["Bandwidth"] != null) oscilloscopeDevice.Bandwidth = jsonObject["Bandwidth"].ToObject<double>();
                    if (jsonObject["SamplingRate"] != null) oscilloscopeDevice.SamplingRate = jsonObject["SamplingRate"].ToObject<double>();
                    if (jsonObject["MemoryDepth"] != null) oscilloscopeDevice.MemoryDepth = jsonObject["MemoryDepth"].ToObject<int>();
                }
                else if (device.GetType().Name == "DmmDevice")
                {
                    var dmmDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) dmmDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["Resolution"] != null) dmmDevice.Resolution = jsonObject["Resolution"].ToObject<double>();
                    if (jsonObject["MeasurementRange"] != null) dmmDevice.MeasurementRange = jsonObject["MeasurementRange"].ToString();
                    if (jsonObject["MeasurementType"] != null) dmmDevice.MeasurementType = jsonObject["MeasurementType"].ToString();
                }
                else if (device.GetType().Name == "AnalogAcquisitionDevice")
                {
                    var analogDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) analogDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["Resolution"] != null) analogDevice.Resolution = jsonObject["Resolution"].ToObject<int>();
                    if (jsonObject["SampleRate"] != null) analogDevice.SampleRate = jsonObject["SampleRate"].ToObject<double>();
                    if (jsonObject["InputRange"] != null) analogDevice.InputRange = jsonObject["InputRange"].ToObject<string>();
                    
                    // 反序列化AnalogInputNode子节点
                    if (jsonObject["AnalogInputNode"] is JObject analogInputNodeObj)
                    {
                        analogDevice.AnalogInputNode = analogInputNodeObj.ToObject<AnalogInputNode>();
                    }
                }
                else if (device.GetType().Name == "AnalogOutputDevice")
                {
                    var analogOutputDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) analogOutputDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["Resolution"] != null) analogOutputDevice.Resolution = jsonObject["Resolution"].ToObject<int>();
                    if (jsonObject["UpdateRate"] != null) analogOutputDevice.UpdateRate = jsonObject["UpdateRate"].ToObject<double>();
                    if (jsonObject["OutputRange"] != null) analogOutputDevice.OutputRange = jsonObject["OutputRange"].ToObject<string>();
                    
                    // 反序列化AnalogOutputNode子节点
                    if (jsonObject["AnalogOutputNode"] is JObject analogOutputNodeObj)
                    {
                        analogOutputDevice.AnalogOutputNode = analogOutputNodeObj.ToObject<AnalogOutputNode>();
                    }
                }
                else if (device.GetType().Name == "SwitchDevice")
                {
                    var switchDevice = device as dynamic;
                    if (jsonObject["ChannelCount"] != null) switchDevice.ChannelCount = jsonObject["ChannelCount"].ToObject<int>();
                    if (jsonObject["SwitchType"] != null) switchDevice.SwitchType = jsonObject["SwitchType"].ToString();
                    if (jsonObject["MaxVoltage"] != null) switchDevice.MaxVoltage = jsonObject["MaxVoltage"].ToObject<double>();
                    if (jsonObject["MaxCurrent"] != null) switchDevice.MaxCurrent = jsonObject["MaxCurrent"].ToObject<double>();
                    
                    // 反序列化SwitchChannelNode子节点
                    if (jsonObject["SwitchChannelNode"] is JObject switchChannelNodeObj)
                    {
                        switchDevice.SwitchChannelNode = switchChannelNodeObj.ToObject<SwitchChannelNode>();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

    }
}
