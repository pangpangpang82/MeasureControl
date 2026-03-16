using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace MeasureControl.Services
{
    /// <summary>
    /// 优化的集合管理器 - 提供高效的集合操作和内存管理
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    public class OptimizedCollectionManager<T> : INotifyCollectionChanged, INotifyPropertyChanged, IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
    {
        #region Private Fields

        private readonly List<T> _items;
        private readonly object _lock = new object();
        private bool _suppressNotifications = false;
        private int _maxSize = int.MaxValue;
        private bool _autoCleanup = true;

        #endregion

        #region Events

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Properties

        /// <summary>
        /// 集合大小
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        /// <summary>
        /// 是否为只读集合
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// 最大集合大小
        /// </summary>
        public int MaxSize
        {
            get => _maxSize;
            set
            {
                if (_maxSize != value)
                {
                    _maxSize = value;
                    OnPropertyChanged(nameof(MaxSize));
                    
                    // 如果新的大小小于当前大小，需要清理
                    if (_maxSize < Count)
                    {
                        TrimToSize(_maxSize);
                    }
                }
            }
        }

        /// <summary>
        /// 是否启用自动清理
        /// </summary>
        public bool AutoCleanup
        {
            get => _autoCleanup;
            set => _autoCleanup = value;
        }

        #endregion

        #region Indexer

        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _items[index];
                }
            }
            set
            {
                lock (_lock)
                {
                    var oldItem = _items[index];
                    _items[index] = value;
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem, index));
                }
            }
        }

        #endregion

        #region Constructor

        public OptimizedCollectionManager(int maxSize = int.MaxValue, bool autoCleanup = true)
        {
            _items = new List<T>();
            _maxSize = maxSize;
            _autoCleanup = autoCleanup;
        }

        public OptimizedCollectionManager(IEnumerable<T> collection, int maxSize = int.MaxValue, bool autoCleanup = true)
        {
            _items = new List<T>(collection ?? throw new ArgumentNullException(nameof(collection)));
            _maxSize = maxSize;
            _autoCleanup = autoCleanup;
            
            // 如果初始大小超过最大大小，需要清理
            if (_maxSize < Count)
            {
                TrimToSize(_maxSize);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 添加元素
        /// </summary>
        /// <param name="item">要添加的元素</param>
        public void Add(T item)
        {
            lock (_lock)
            {
                _items.Add(item);
                
                // 检查是否需要清理
                if (_autoCleanup && _items.Count > _maxSize)
                {
                    TrimToSize(_maxSize);
                }
                else
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _items.Count - 1));
                }
            }
        }

        /// <summary>
        /// 批量添加元素
        /// </summary>
        /// <param name="items">要添加的元素集合</param>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            lock (_lock)
            {
                _suppressNotifications = true;
                try
                {
                    var itemsList = items.ToList();
                    _items.AddRange(itemsList);
                    
                    // 检查是否需要清理
                    if (_autoCleanup && _items.Count > _maxSize)
                    {
                        TrimToSize(_maxSize);
                    }
                }
                finally
                {
                    _suppressNotifications = false;
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }

        /// <summary>
        /// 移除元素
        /// </summary>
        /// <param name="item">要移除的元素</param>
        /// <returns>是否成功移除</returns>
        public bool Remove(T item)
        {
            lock (_lock)
            {
                var idx = _items.IndexOf(item);
                if (idx >= 0)
                {
                    _items.RemoveAt(idx);
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, idx));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 移除指定索引的元素
        /// </summary>
        /// <param name="index">要移除的元素索引</param>
        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _items.Count)
                {
                    var item = _items[index];
                    _items.RemoveAt(index);
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                }
            }
        }

        /// <summary>
        /// 清空集合
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        /// <summary>
        /// 检查是否包含指定元素
        /// </summary>
        /// <param name="item">要检查的元素</param>
        /// <returns>是否包含</returns>
        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _items.Contains(item);
            }
        }

        /// <summary>
        /// 复制到数组
        /// </summary>
        /// <param name="array">目标数组</param>
        /// <param name="arrayIndex">起始索引</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock)
            {
                _items.CopyTo(array, arrayIndex);
            }
        }

        /// <summary>
        /// 获取枚举器
        /// </summary>
        /// <returns>枚举器</returns>
        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                return _items.ToList().GetEnumerator();
            }
        }

        /// <summary>
        /// 获取枚举器（非泛型）
        /// </summary>
        /// <returns>枚举器</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// 查找元素索引
        /// </summary>
        /// <param name="item">要查找的元素</param>
        /// <returns>元素索引，未找到返回-1</returns>
        public int IndexOf(T item)
        {
            lock (_lock)
            {
                return _items.IndexOf(item);
            }
        }

        /// <summary>
        /// 在指定位置插入元素
        /// </summary>
        /// <param name="index">插入位置</param>
        /// <param name="item">要插入的元素</param>
        public void Insert(int index, T item)
        {
            lock (_lock)
            {
                _items.Insert(index, item);
                
                // 检查是否需要清理
                if (_autoCleanup && _items.Count > _maxSize)
                {
                    TrimToSize(_maxSize);
                }
                else
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
                }
            }
        }

        /// <summary>
        /// 修剪集合到指定大小
        /// </summary>
        /// <param name="size">目标大小</param>
        public void TrimToSize(int size)
        {
            lock (_lock)
            {
                if (size < 0)
                    throw new ArgumentException("大小不能为负数", nameof(size));

                if (_items.Count > size)
                {
                    var removedItems = _items.Skip(size).ToList();
                    _items.RemoveRange(size, _items.Count - size);
                    
                    if (!_suppressNotifications)
                    {
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    }
                }
            }
        }

        /// <summary>
        /// 获取集合的快照
        /// </summary>
        /// <returns>集合快照</returns>
        public List<T> GetSnapshot()
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }

        /// <summary>
        /// 转换为ObservableCollection
        /// </summary>
        /// <returns>ObservableCollection</returns>
        public ObservableCollection<T> ToObservableCollection()
        {
            lock (_lock)
            {
                return new ObservableCollection<T>(_items);
            }
        }

        #endregion

        #region Private Methods

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
            {
                CollectionChanged?.Invoke(this, e);
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 集合工厂 - 创建不同类型的优化集合
    /// </summary>
    public static class CollectionFactory
    {
        /// <summary>
        /// 创建优化的集合
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="maxSize">最大大小</param>
        /// <param name="autoCleanup">是否自动清理</param>
        /// <returns>优化的集合</returns>
        public static OptimizedCollectionManager<T> CreateOptimizedCollection<T>(int maxSize = int.MaxValue, bool autoCleanup = true)
        {
            return new OptimizedCollectionManager<T>(maxSize, autoCleanup);
        }

        /// <summary>
        /// 创建只读集合
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="items">初始元素</param>
        /// <returns>只读集合</returns>
        public static IReadOnlyList<T> CreateReadOnlyCollection<T>(IEnumerable<T> items)
        {
            return items?.ToList().AsReadOnly() ?? new List<T>().AsReadOnly();
        }

        /// <summary>
        /// 创建延迟加载集合
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="loader">加载函数</param>
        /// <returns>延迟加载集合</returns>
        public static Lazy<IEnumerable<T>> CreateLazyCollection<T>(Func<IEnumerable<T>> loader)
        {
            return new Lazy<IEnumerable<T>>(loader);
        }
    }
}
