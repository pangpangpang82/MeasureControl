using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers
{
    public static class DeviceDisplayFieldRegistry
    {
        private sealed class Entry
        {
            public int Priority { get; set; }
            public Func<DeviceBase, bool> Match { get; set; }
            public Func<DeviceBase, ObservableCollection<DeviceDisplayField>> Factory { get; set; }
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _initialized;

        static DeviceDisplayFieldRegistry()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 机箱/控制器
            Register(device => device is ChassisDevice, CreateChassisFields, priority: 900);
            Register(device => device is ControllerDevice, CreateControllerFields, priority: 850);

            // PXI 板卡（具体类型优先）
            Register(device => device is SwitchDevice, CreateSwitchFields, priority: 820);
            Register(device => device is DigitalIODevice, CreateDigitalIoFields, priority: 820);
            Register(device => device is AnalogAcquisitionDevice, CreateAnalogAcquisitionFields, priority: 820);
            Register(device => device is AnalogOutputDevice, CreateAnalogOutputFields, priority: 820);
            Register(device => device is ProgrammableResistorDevice, CreateProgrammableResistorFields, priority: 820);

            // 仪器（具体类型优先）
            Register(device => device is PowerSupplyDevice, CreatePowerSupplyFields, priority: 800);
            Register(device => device is ElectronicLoadDevice, CreateElectronicLoadFields, priority: 800);
            Register(device => device is DmmDevice, CreateDmmFields, priority: 800);
            Register(device => device is SignalGeneratorDevice, CreateSignalGeneratorFields, priority: 800);
            Register(device => device is OscilloscopeDevice, CreateOscilloscopeFields, priority: 800);
            Register(device => device is FrequencyCounterDevice, CreateFrequencyCounterFields, priority: 800);

            Register(device => device is DeviceBase, CreateCommonFields, priority: 700);

        }

        private static ObservableCollection<DeviceDisplayField> CreateCommonFields(DeviceBase device)
        {
            var list = CreateCommonHeader(device);
            return list;
        }

        public static void Register(Func<DeviceBase, bool> match, Func<DeviceBase, ObservableCollection<DeviceDisplayField>> factory, int priority = 0)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _entries.Add(new Entry
            {
                Priority = priority,
                Match = match,
                Factory = factory
            });

            _entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static bool TryGetDisplayFields(DeviceBase device, out ObservableCollection<DeviceDisplayField> fields)
        {
            fields = null;
            if (device == null) return false;

            EnsureInitialized();

            foreach (var entry in _entries)
            {
                bool matched;
                try
                {
                    matched = entry.Match(device);
                }
                catch
                {
                    continue;
                }

                if (!matched) continue;

                try
                {
                    fields = entry.Factory(device);
                }
                catch
                {
                    fields = null;
                }

                if (fields != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static ObservableCollection<DeviceDisplayField> CreateCommonHeader(DeviceBase device)
        {
            var list = new ObservableCollection<DeviceDisplayField>();
            var displayName = device.PrimaryDisplayName;
            Add(list, "名称", displayName, "名称: {0}");
            Add(list, "制造商", device.Manufacturer, "制造商: {0}");
            Add(list, "型号", device.Model, "型号: {0}");
            return list;
        }

        private static bool IsEmptySlotPlaceholder(DeviceBase device)
        {
            if (device == null) return false;
            if (string.Equals(device.Name, "空槽", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrWhiteSpace(device.CardName) &&
                string.IsNullOrWhiteSpace(device.ParentNode) &&
                string.IsNullOrWhiteSpace(device.Model) &&
                !string.IsNullOrWhiteSpace(device.SlotPosition))
            {
                return true;
            }
            return false;
        }

        private static ObservableCollection<DeviceDisplayField> CreateChassisFields(DeviceBase device)
        {
            var chassis = device as ChassisDevice;
            if (chassis == null) return null;

            var list = CreateCommonHeader(chassis);
            Add(list, "槽位数", chassis.SlotCount.ToString(), "槽位数: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateControllerFields(DeviceBase device)
        {
            var controller = device as ControllerDevice;
            if (controller == null) return null;

            var list = CreateCommonHeader(controller);
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateSwitchFields(DeviceBase device)
        {
            var sw = device as SwitchDevice;
            if (sw == null) return null;

            var list = CreateCommonHeader(sw);
            Add(list, "矩阵配置", sw.MatrixConfiguration, "矩阵配置: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateDigitalIoFields(DeviceBase device)
        {
            var dio = device as DigitalIODevice;
            if (dio == null) return null;

            var list = CreateCommonHeader(dio);
            Add(list, "DI通道数", dio.InputChannels > 0 ? dio.InputChannels.ToString() : string.Empty, "DI通道数: {0}");
            Add(list, "DO通道数", dio.OutputChannels > 0 ? dio.OutputChannels.ToString() : string.Empty, "DO通道数: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateAnalogAcquisitionFields(DeviceBase device)
        {
            var ai = device as AnalogAcquisitionDevice;
            if (ai == null) return null;

            var list = CreateCommonHeader(ai);
            Add(list, "通道数", ai.ChannelCount > 0 ? ai.ChannelCount.ToString() : string.Empty, "通道数: {0}");
            Add(list, "采样率上限", $"{ai.MaxSampleRate} Hz", "采样率上限: {0}");
            Add(list, "输入量程", ai.InputRange, "输入量程: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateAnalogOutputFields(DeviceBase device)
        {
            var ao = device as AnalogOutputDevice;
            if (ao == null) return null;

            var list = CreateCommonHeader(ao);
            Add(list, "通道数", ao.ChannelCount > 0 ? ao.ChannelCount.ToString() : string.Empty, "通道数: {0}");
            Add(list, "采样率上限", $"{ao.MaxSampleRate} Hz", "采样率上限: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateProgrammableResistorFields(DeviceBase device)
        {
            var pr = device as ProgrammableResistorDevice;
            if (pr == null) return null;

            var list = CreateCommonHeader(pr);
            Add(list, "通道数", pr.ChannelCount > 0 ? pr.ChannelCount.ToString() : string.Empty, "通道数: {0}");
            Add(list, "最小电阻", pr.MinResistance > 0 ? $"{pr.MinResistance} Ω" : string.Empty, "最小电阻: {0}");
            Add(list, "最大电阻", pr.MaxResistance > 0 ? $"{pr.MaxResistance} Ω" : string.Empty, "最大电阻: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreatePowerSupplyFields(DeviceBase device)
        {
            var ps = device as PowerSupplyDevice;
            if (ps == null) return null;

            var list = CreateCommonHeader(ps);
            Add(list, "通道数", ps.ChannelCount > 0 ? ps.ChannelCount.ToString() : string.Empty, "通道数: {0}");
            Add(list, "最大电压", ps.MaxVoltage > 0 ? $"{ps.MaxVoltage} V" : string.Empty, "最大电压: {0}");
            Add(list, "最大电流", ps.MaxCurrent > 0 ? $"{ps.MaxCurrent} A" : string.Empty, "最大电流: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateElectronicLoadFields(DeviceBase device)
        {
            var el = device as ElectronicLoadDevice;
            if (el == null) return null;

            var list = CreateCommonHeader(el);
            Add(list, "通道数", el.ChannelCount > 0 ? el.ChannelCount.ToString() : string.Empty, "通道数: {0}");
            Add(list, "最大电压", el.MaxVoltage > 0 ? $"{el.MaxVoltage} V" : string.Empty, "最大电压: {0}");
            Add(list, "最大电流", el.MaxCurrent > 0 ? $"{el.MaxCurrent} A" : string.Empty, "最大电流: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateDmmFields(DeviceBase device)
        {
            var dmm = device as DmmDevice;
            if (dmm == null) return null;

            var list = CreateCommonHeader(dmm);
            Add(list, "分辨率", dmm.Resolution > 0 ? dmm.Resolution.ToString() : string.Empty, "分辨率: {0}");
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateSignalGeneratorFields(DeviceBase device)
        {
            var sg = device as SignalGeneratorDevice;
            if (sg == null) return null;

            var list = CreateCommonHeader(sg);
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateOscilloscopeFields(DeviceBase device)
        {
            var osc = device as OscilloscopeDevice;
            if (osc == null) return null;

            var list = CreateCommonHeader(osc);
            return list;
        }

        private static ObservableCollection<DeviceDisplayField> CreateFrequencyCounterFields(DeviceBase device)
        {
            var fc = device as FrequencyCounterDevice;
            if (fc == null) return null;

            var list = CreateCommonHeader(fc);
            return list;
        }

        private static void Add(ObservableCollection<DeviceDisplayField> list, string label, string value, string format)
        {
            if (list == null) return;
            list.Add(new DeviceDisplayField(label, value ?? string.Empty, format));
        }
    }
}
