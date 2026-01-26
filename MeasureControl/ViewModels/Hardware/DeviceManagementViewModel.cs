using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Hardware
{
    /// <summary>
    /// 设备管理ViewModel - 负责设备选择和详细信息显示
    /// </summary>
    public class DeviceManagementViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;

        private DeviceBase _selectedDevice;
        private bool _isDeviceDetailsVisible;
        private bool _isDetailsVisible;
        private string _deviceInfoTitle;
        private string _deviceField1;
        private string _deviceField2;
        private string _deviceField3;
        private string _deviceField4;
        private string _deviceField5;
        private string _deviceField6;

        #endregion

        #region Public Properties

        /// <summary>
        /// 选中的设备
        /// </summary>
        public DeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    UpdateDeviceDetails();
                }
            }
        }

        /// <summary>
        /// 设备详细信息是否可见
        /// </summary>
        public bool IsDeviceDetailsVisible
        {
            get => _isDeviceDetailsVisible;
            set => SetProperty(ref _isDeviceDetailsVisible, value);
        }

        /// <summary>
        /// 详细信息面板是否可见（统一控制）
        /// </summary>
        public bool IsDetailsVisible
        {
            get => _isDetailsVisible;
            set => SetProperty(ref _isDetailsVisible, value);
        }

        /// <summary>
        /// 设备信息标题
        /// </summary>
        public string DeviceInfoTitle
        {
            get => _deviceInfoTitle;
            set => SetProperty(ref _deviceInfoTitle, value);
        }

        /// <summary>
        /// 动态字段1
        /// </summary>
        public string DeviceField1
        {
            get => _deviceField1;
            set => SetProperty(ref _deviceField1, value);
        }

        /// <summary>
        /// 动态字段2
        /// </summary>
        public string DeviceField2
        {
            get => _deviceField2;
            set => SetProperty(ref _deviceField2, value);
        }

        /// <summary>
        /// 动态字段3
        /// </summary>
        public string DeviceField3
        {
            get => _deviceField3;
            set => SetProperty(ref _deviceField3, value);
        }

        /// <summary>
        /// 动态字段4
        /// </summary>
        public string DeviceField4
        {
            get => _deviceField4;
            set => SetProperty(ref _deviceField4, value);
        }

        /// <summary>
        /// 动态字段5
        /// </summary>
        public string DeviceField5
        {
            get => _deviceField5;
            set => SetProperty(ref _deviceField5, value);
        }

        /// <summary>
        /// 动态字段6
        /// </summary>
        public string DeviceField6
        {
            get => _deviceField6;
            set => SetProperty(ref _deviceField6, value);
        }

        #endregion

        #region Commands

        public ICommand SelectDeviceCommand { get; private set; }
        public ICommand ClearDeviceSelectionCommand { get; private set; }

        #endregion

        #region Constructor

        public DeviceManagementViewModel(IEventAggregator eventAggregator, IDialogService dialogService)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCommands();
            SubscribeToEvents();
        }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            SelectDeviceCommand = new DelegateCommand<DeviceBase>(OnSelectDevice);
            ClearDeviceSelectionCommand = new DelegateCommand(OnClearDeviceSelection);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<DeviceSelectedEvent>().Subscribe(device => OnDeviceSelected(device));
        }

        private void UpdateDeviceDetails()
        {
            if (SelectedDevice == null)
            {
                ClearDeviceDetails();
                return;
            }

            try
            {
                DeviceInfoTitle = $"设备信息 - {SelectedDevice.Name}";
                IsDeviceDetailsVisible = true;
                IsDetailsVisible = true;

                // 根据设备类型设置不同的字段
                switch (SelectedDevice.DeviceType)
                {
                    case "Chassis":
                        SetChassisDetails();
                        break;
                    case "Card":
                        SetCardDetails();
                        break;
                    case "Instrument":
                        SetInstrumentDetails();
                        break;
                    default:
                        SetDefaultDetails();
                        break;
                }
            }
            catch (Exception)
            {
                ClearDeviceDetails();
            }
        }

        private void SetChassisDetails()
        {
            DeviceField1 = $"型号: {SelectedDevice.Model}";
            DeviceField2 = $"状态: {SelectedDevice.Status}";
            DeviceField3 = $"设备类型: 机箱";
            DeviceField4 = $"子设备数量: {SelectedDevice.Children?.Count ?? 0}";
            DeviceField5 = "";
            DeviceField6 = "";
        }

        private void SetCardDetails()
        {
            DeviceField1 = $"型号: {SelectedDevice.Model}";
            DeviceField2 = $"父节点: {SelectedDevice.ParentNode}";
            DeviceField3 = $"连接方式: {SelectedDevice.ConnectionMethod}";
            DeviceField4 = $"状态: {SelectedDevice.Status}";
            DeviceField5 = "";
            DeviceField6 = "";
        }

        private void SetInstrumentDetails()
        {
            DeviceField1 = $"型号: {SelectedDevice.Model}";
            DeviceField2 = $"父节点: {SelectedDevice.ParentNode}";
            DeviceField3 = $"连接方式: {SelectedDevice.ConnectionMethod}";
            DeviceField4 = "";
            DeviceField5 = "";
            DeviceField6 = "";
        }

        private void SetDefaultDetails()
        {
            DeviceField1 = $"名称: {SelectedDevice.Name}";
            DeviceField2 = $"类型: {SelectedDevice.DeviceType}";
            DeviceField3 = "";
            DeviceField4 = "";
            DeviceField5 = "";
            DeviceField6 = "";
        }

        private void ClearDeviceDetails()
        {
            DeviceInfoTitle = "设备信息";
            DeviceField1 = "";
            DeviceField2 = "";
            DeviceField3 = "";
            DeviceField4 = "";
            DeviceField5 = "";
            DeviceField6 = "";
            IsDeviceDetailsVisible = false;
            IsDetailsVisible = false;
        }

        #endregion

        #region Command Implementations

        private void OnSelectDevice(DeviceBase device)
        {
            SelectedDevice = device;
            _eventAggregator.GetEvent<DeviceSelectedEvent>().Publish(device);
        }

        private void OnClearDeviceSelection()
        {
            SelectedDevice = null;
            ClearDeviceDetails();
        }

        #endregion

        #region Event Handlers

        private void OnDeviceSelected(DeviceBase device)
        {
            if (device != SelectedDevice)
            {
                SelectedDevice = device;
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 清理选中的设备
                SelectedDevice = null;
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
