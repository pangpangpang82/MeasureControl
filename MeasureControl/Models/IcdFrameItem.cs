using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// ICD帧配置项
    /// </summary>
    public class IcdFrameItem : BindableBase
    {
        private int _;
        private string _frameName;
        private string _frameId;
        private string _protocol;
        private string _remarks;
        private ObservableCollection<IcdFrameField> _fields;
        private bool _isEmpty;
        private bool _isSelected;

        public int Index
        {
            get => _;
            set => SetProperty(ref _, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string FrameName
        {
            get => _frameName;
            set => SetProperty(ref _frameName, value);
        }

        public string FrameId
        {
            get => _frameId;
            set => SetProperty(ref _frameId, value);
        }

        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        /// <summary>
        /// 获取帧ID段中"帧ID (Hex)"配置项的值（用于水印显示）
        /// </summary>
        public string FrameIdFieldValue
        {
            get
            {
                if (Fields != null)
                {
                    var frameIdField = Fields.FirstOrDefault(f => f.DisplayName == "帧ID段");
                    if (frameIdField != null && frameIdField.ConfigItems != null)
                    {
                        var frameIdConfigItem = frameIdField.ConfigItems.FirstOrDefault(c => c.Name == "帧ID (Hex)");
                        if (frameIdConfigItem != null)
                        {
                            return frameIdConfigItem.Value ?? string.Empty;
                        }
                    }
                }
                return string.Empty;
            }
        }

        private readonly Dictionary<ObservableCollection<IcdFieldConfigItem>, IcdFrameField> _configCollectionOwners =
            new Dictionary<ObservableCollection<IcdFieldConfigItem>, IcdFrameField>();

        public ObservableCollection<IcdFrameField> Fields
        {
            get => _fields;
            set
            {
                if (ReferenceEquals(_fields, value))
                    return;

                DetachFields(_fields);
                _fields = value ?? new ObservableCollection<IcdFrameField>();
                AttachFields(_fields);
                RaisePropertyChanged(nameof(Fields));
                RaisePropertyChanged(nameof(FrameIdFieldValue));
            }
        }

        public IcdFrameItem()
        {
            Fields = new ObservableCollection<IcdFrameField>();
        }

        private void AttachFields(ObservableCollection<IcdFrameField> fields)
        {
            if (fields == null)
                return;

            fields.CollectionChanged += Fields_CollectionChanged;
            foreach (var field in fields)
            {
                AttachField(field);
            }
        }

        private void DetachFields(ObservableCollection<IcdFrameField> fields)
        {
            if (fields == null)
                return;

            fields.CollectionChanged -= Fields_CollectionChanged;
            foreach (var field in fields)
            {
                DetachField(field);
            }
        }

        private void Fields_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (IcdFrameField field in e.OldItems)
                {
                    DetachField(field);
                }
            }

            if (e.NewItems != null)
            {
                foreach (IcdFrameField field in e.NewItems)
                {
                    AttachField(field);
                }
            }

            RaisePropertyChanged(nameof(FrameIdFieldValue));
        }

        private void AttachField(IcdFrameField field)
        {
            if (field == null)
                return;

            field.PropertyChanged += Field_PropertyChanged;
            AttachConfigItems(field);
        }

        private void DetachField(IcdFrameField field)
        {
            if (field == null)
                return;

            field.PropertyChanged -= Field_PropertyChanged;
            DetachConfigItems(field);
        }

        private void Field_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IcdFrameField.ConfigItems))
            {
                var field = sender as IcdFrameField;
                DetachConfigItems(field);
                AttachConfigItems(field);
                RaisePropertyChanged(nameof(FrameIdFieldValue));
            }
        }

        private void AttachConfigItems(IcdFrameField field)
        {
            if (field?.ConfigItems == null)
                return;

            var collection = field.ConfigItems;
            if (!_configCollectionOwners.ContainsKey(collection))
            {
                _configCollectionOwners[collection] = field;
                collection.CollectionChanged += ConfigItems_CollectionChanged;
            }

            foreach (var item in collection)
            {
                if (item != null)
                {
                    item.PropertyChanged += ConfigItem_PropertyChanged;
                }
            }
        }

        private void DetachConfigItems(IcdFrameField field)
        {
            if (field?.ConfigItems == null)
                return;

            var collection = field.ConfigItems;
            if (_configCollectionOwners.ContainsKey(collection))
            {
                collection.CollectionChanged -= ConfigItems_CollectionChanged;
                _configCollectionOwners.Remove(collection);
            }

            foreach (var item in collection)
            {
                if (item != null)
                {
                    item.PropertyChanged -= ConfigItem_PropertyChanged;
                }
            }
        }

        private void ConfigItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (IcdFieldConfigItem item in e.OldItems)
                {
                    if (item != null)
                    {
                        item.PropertyChanged -= ConfigItem_PropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (IcdFieldConfigItem item in e.NewItems)
                {
                    if (item != null)
                    {
                        item.PropertyChanged += ConfigItem_PropertyChanged;
                    }
                }
            }

            RaisePropertyChanged(nameof(FrameIdFieldValue));
        }

        private void ConfigItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IcdFieldConfigItem.Value))
            {
                RaisePropertyChanged(nameof(FrameIdFieldValue));
            }
        }
    }

    /// <summary>
    /// ICD帧字段
    /// </summary>
    public class IcdFrameField : BindableBase
    {
        private string _name;
        private string _displayName;
        private string _backgroundColor;
        private ObservableCollection<IcdFieldConfigItem> _configItems;
        private bool _isSelected;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string BackgroundColor
        {
            get => _backgroundColor;
            set => SetProperty(ref _backgroundColor, value);
        }

        public ObservableCollection<IcdFieldConfigItem> ConfigItems
        {
            get => _configItems;
            set => SetProperty(ref _configItems, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    var label = DisplayName ?? Name ?? "(unnamed)";
                    Debug.WriteLine($"[ICD] Field '{label}' IsSelected -> {value}");
                    Trace.WriteLine($"[ICD] Field '{label}' IsSelected -> {value}");
                }
            }
        }

        public IcdFrameField()
        {
            ConfigItems = new ObservableCollection<IcdFieldConfigItem>();
        }
    }

    /// <summary>
    /// ICD字段配置项
    /// </summary>
    public class IcdFieldConfigItem : BindableBase
    {
        private string _name;
        private string _value;
        private string _configType; // ComboBox, TextBox, etc.
        private ObservableCollection<string> _options; // For ComboBox
        private bool _isVisible = true;
        private bool _isReadOnly = false;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public string ConfigType
        {
            get => _configType;
            set => SetProperty(ref _configType, value);
        }

        public ObservableCollection<string> Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// 指示该配置项是否只读（用于界面禁用编辑）。
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set => SetProperty(ref _isReadOnly, value);
        }

        private bool _hasValidationError;

        /// <summary>
        /// 标记该配置项是否存在校验错误，用于在界面上展示红色边框等提示。
        /// </summary>
        [JsonIgnore]
        public bool HasValidationError
        {
            get => _hasValidationError;
            set => SetProperty(ref _hasValidationError, value);
        }

        public IcdFieldConfigItem()
        {
            Options = new ObservableCollection<string>();
            _isVisible = true;
            _hasValidationError = false;
            _isReadOnly = false;
        }
    }
}

