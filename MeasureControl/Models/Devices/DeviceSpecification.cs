using System;
using System.Collections.Generic;
using System.Linq;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 设备规格管理类，用于统一管理和显示设备的详细规格参数
    /// </summary>
    public class DeviceSpecification
    {
        private readonly List<SpecificationItem> _items;

        public DeviceSpecification()
        {
            _items = new List<SpecificationItem>();
        }

        /// <summary>
        /// 添加规格项
        /// </summary>
        public void Add(string name, string value, string category = "")
        {
            if (!string.IsNullOrEmpty(value) && value != "N/A")
            {
                _items.Add(new SpecificationItem(name, value, category));
            }
        }

        /// <summary>
        /// 批量添加规格项（从字典）
        /// </summary>
        public void AddRange(Dictionary<string, string> specs, string category = "")
        {
            foreach (var spec in specs)
            {
                Add(spec.Key, spec.Value, category);
            }
        }

        /// <summary>
        /// 获取所有规格项
        /// </summary>
        public IEnumerable<SpecificationItem> GetAll()
        {
            return _items.AsReadOnly();
        }

        /// <summary>
        /// 按分组获取规格项
        /// </summary>
        public IEnumerable<SpecificationItem> GetByCategory(string category)
        {
            return _items.Where(item => item.Category == category);
        }

        /// <summary>
        /// 获取所有分组名称
        /// </summary>
        public IEnumerable<string> GetCategories()
        {
            return _items.Select(item => item.Category).Distinct().Where(c => !string.IsNullOrEmpty(c));
        }

        /// <summary>
        /// 根据规格名称获取其值，未找到返回空字符串
        /// </summary>
        public string GetValue(string name)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            return item?.Value ?? string.Empty;
        }

        /// <summary>
        /// 清空所有规格项
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// 获取规格项数量
        /// </summary>
        public int Count => _items.Count;
    }

    /// <summary>
    /// 单个规格项，表示设备的一个规格参数
    /// </summary>
    public class SpecificationItem
    {
        /// <summary>
        /// 规格名称（如"最大电压"）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 规格值（如"300V"）
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 规格分组（如"电气规格"、"继电器性能"等）
        /// </summary>
        public string Category { get; }

        public SpecificationItem(string name, string value, string category = "")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Category = category ?? string.Empty;
        }

        public override string ToString()
        {
            return $"{Name}: {Value}";
        }
    }
}

