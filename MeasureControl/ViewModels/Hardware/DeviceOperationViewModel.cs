using System;
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
    /// 设备操作ViewModel - 负责设备添加、删除、选择等操作
    /// </summary>
    public class DeviceOperationViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;

        private ObservableCollection<DeviceBase> _chassisDevices;
        private DeviceBase _selectedDevice;
        private ObservableCollection<ProjectItem> _tools;

        #endregion

        #region Public Properties

        /// <summary>
        /// 机箱设备列表
        /// </summary>
        public ObservableCollection<DeviceBase> ChassisDevices
        {
            get => _chassisDevices;
            set => SetProperty(ref _chassisDevices, value);
        }

        /// <summary>
        /// 选中的设备
        /// </summary>
        public DeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        /// <summary>
        /// 工具集合
        /// </summary>
        public ObservableCollection<ProjectItem> Tools
        {
            get => _tools;
            set => SetProperty(ref _tools, value);
        }

        #endregion

        #region Commands

        public ICommand AddDeviceCommand { get; private set; }
        public ICommand DeviceDoubleClickCommand { get; private set; }
        public ICommand DeviceClickCommand { get; private set; }
        public ICommand ToggleDeviceExpansionCommand { get; private set; }
        public ICommand SelectDeviceCommand { get; private set; }
        public ICommand DeleteDeviceCommand { get; private set; }
        public ICommand ClearDeviceSelectionCommand { get; private set; }

        #endregion

        #region Constructor

        public DeviceOperationViewModel(
            IPxiChassisService pxiChassisService,
            IEventAggregator eventAggregator,
            IDialogService dialogService)
        {
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCollections();
            InitializeCommands();
            SubscribeToEvents();
            InitializeTools();
        }

        #endregion

        #region Private Methods

        private void InitializeCollections()
        {
            ChassisDevices = new ObservableCollection<DeviceBase>();
            Tools = new ObservableCollection<ProjectItem>();
        }

        private void InitializeCommands()
        {
            AddDeviceCommand = new DelegateCommand<ProjectItem>(OnAddDevice);
            DeviceDoubleClickCommand = new DelegateCommand<DeviceBase>(OnDeviceDoubleClick);
            DeviceClickCommand = new DelegateCommand<DeviceBase>(OnDeviceClick);
            ToggleDeviceExpansionCommand = new DelegateCommand<DeviceBase>(OnToggleDeviceExpansion);
            SelectDeviceCommand = new DelegateCommand<DeviceBase>(OnSelectDevice);
            DeleteDeviceCommand = new DelegateCommand<DeviceBase>(OnDeleteDevice);
            ClearDeviceSelectionCommand = new DelegateCommand(OnClearDeviceSelection);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Subscribe(OnPxiChassisSelected);
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Subscribe(OnDeviceModified);
        }

        private void InitializeTools()
        {
            // 初始化工具树（示例数据）
            var rootNode = new ProjectItem
            {
                Name = "工具",
                Icon = "/Resources/Logo/folder.png",
                Type = "root",
                Tag = "Root"
            };

            // 添加PXI机箱工具
            var pxiChassisNode = new ProjectItem
            {
                Name = "PXI机箱",
                Icon = "/Resources/Logo/chassis.png",
                Type = "pxi_chassis",
                Tag = "PxiChassis"
            };

            // 添加9槽机箱设备
            var chassisDevice = new ProjectItem
            {
                Name = "PXIe-2722G2",
                Icon = "/Resources/Logo/chassis_device.png",
                Type = "chassis_device",
                Tag = "ChassisDevice"
            };

            // 添加PXI板卡设备
            var controllerCard = new ProjectItem
            {
                Name = "PXIe-3987",
                Icon = "/Resources/Logo/controller.png",
                Type = "controller_card",
                Tag = "ControllerCard"
            };

            var switchCard = new ProjectItem
            {
                Name = "PXIe-2527",
                Icon = "/Resources/Logo/switch.png",
                Type = "switch_card",
                Tag = "SwitchCard"
            };

            var dmmCard = new ProjectItem
            {
                Name = "PXIe-4082",
                Icon = "/Resources/Logo/dmm.png",
                Type = "dmm_card",
                Tag = "DmmCard"
            };

            // 添加程控设备
            var powerSupply = new ProjectItem
            {
                Name = "E3631A",
                Icon = "/Resources/Logo/power_supply.png",
                Type = "power_supply",
                Tag = "PowerSupply"
            };

            var electronicLoad = new ProjectItem
            {
                Name = "E4360A",
                Icon = "/Resources/Logo/electronic_load.png",
                Type = "electronic_load",
                Tag = "ElectronicLoad"
            };

            var oscilloscope = new ProjectItem
            {
                Name = "DSOX1204A",
                Icon = "/Resources/Logo/oscilloscope.png",
                Type = "oscilloscope",
                Tag = "Oscilloscope"
            };

            // 构建层次结构 - 只添加机箱设备，不添加默认板卡
            pxiChassisNode.Children.Add(chassisDevice);
            // 移除默认板卡设备，让用户手动添加需要的板卡
            // chassisDevice.Children.Add(controllerCard);
            // chassisDevice.Children.Add(switchCard);
            // chassisDevice.Children.Add(dmmCard);

            rootNode.Children.Add(pxiChassisNode);
            rootNode.Children.Add(powerSupply);
            rootNode.Children.Add(electronicLoad);
            rootNode.Children.Add(oscilloscope);

            Tools.Add(rootNode);
        }

        #endregion

        #region Command Implementations

        private void OnAddDevice(ProjectItem projectItem)
        {
            if (projectItem == null) return;

            try
            {
                // 创建新设备
                var device = DeviceFactory.CreateDevice(projectItem.Name);

                // 根据设备类型设置不同的属性
                if (IsChassisDevice(projectItem))
                {
                    HandleChassisDevice(device, projectItem);
                }
                else if (IsPxiCardDevice(projectItem))
                {
                    HandlePxiCardDevice(device, projectItem);
                }
                else
                {
                    HandleInstrumentDevice(device, projectItem);
                }

                // 将设备添加到服务
                if (device.DeviceType != "Card")
                {
                    // 这里可以添加设备到服务的逻辑
                }

                _eventAggregator.GetEvent<DeviceAddedEvent>().Publish(device);
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"添加设备失败: {ex.Message}", "错误");
            }
        }

        private void HandleChassisDevice(DeviceBase device, ProjectItem projectItem)
        {
            // 检查是否已经有机箱，只能有一个机箱
            if (FindChassisDevice() != null)
            {
                _dialogService.ShowWarningDialog("一个PXI机箱只能添加一个机箱设备", "提示");
                return;
            }

            // ParentNode 已在 ChassisDevice 构造函数中自动设置为 DeviceTypeName
            device.Model = projectItem.Name;
            device.ConnectionMethod = "详细信息";
            device.Details = "详细信息";
            device.DeviceType = "Chassis";
            device.Status = "正常";
            device.IsExpanded = true;

            if (device.Children == null)
                device.Children = new ObservableCollection<DeviceBase>();

            ChassisDevices.Insert(0, device);
        }

        private void HandlePxiCardDevice(DeviceBase device, ProjectItem projectItem)
        {
            var chassisDevice = FindChassisDevice();
            if (chassisDevice == null)
            {
                _dialogService.ShowWarningDialog("请先添加机箱设备", "提示");
                return;
            }

            device.ParentNode = GetParentNodeName(projectItem);
            device.Model = projectItem.Name;
            device.ConnectionMethod = "LAN";
            device.DeviceType = "Card";
            device.Status = "正常";

            if (chassisDevice.Children == null)
                chassisDevice.Children = new ObservableCollection<DeviceBase>();
            chassisDevice.Children.Add(device);
        }

        private void HandleInstrumentDevice(DeviceBase device, ProjectItem projectItem)
        {
            device.ParentNode = GetParentNodeName(projectItem);
            device.Model = projectItem.Name;
            device.ConnectionMethod = "LAN";
            device.DeviceType = "Instrument";
            device.IsExpanded = false;

            ChassisDevices.Add(device);
        }

        private void OnDeviceDoubleClick(DeviceBase device)
        {
            if (device == null) return;

            try
            {
                // 切换设备展开状态
                device.IsExpanded = !device.IsExpanded;
                _eventAggregator.GetEvent<DeviceExpansionToggledEvent>().Publish(device);
            }
            catch (Exception)
            {
            }
        }

        private void OnDeviceClick(DeviceBase device)
        {
            SelectedDevice = device;
            _eventAggregator.GetEvent<DeviceSelectedEvent>().Publish(device);
        }

        private void OnToggleDeviceExpansion(DeviceBase device)
        {
            OnDeviceDoubleClick(device);
        }

        private void OnSelectDevice(DeviceBase device)
        {
            OnDeviceClick(device);
        }

        private void OnDeleteDevice(DeviceBase device)
        {
            if (device == null) return;

            try
            {
                if (device.DeviceType == "Card")
                {
                    // 从父设备的子设备中移除
                    var parentDevice = FindParentDevice(device);
                    parentDevice?.Children?.Remove(device);
                }
                else
                {
                    // 从主设备列表中移除
                    ChassisDevices.Remove(device);
                }

                _eventAggregator.GetEvent<DeviceDeletedEvent>().Publish(device);
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"删除设备失败: {ex.Message}", "错误");
            }
        }

        private void OnClearDeviceSelection()
        {
            SelectedDevice = null;
        }

        #endregion

        #region Event Handlers

        private void OnPxiChassisSelected(PxiChassisSelectedEventArgs args)
        {
            if (args?.ChassisName != null)
            {
                // 加载选中机箱的设备
                LoadChassisDevices(args.ChassisName);
            }
        }

        private void OnDeviceModified(DeviceModifiedEventArgs args)
        {
            // 设备修改时的处理逻辑
        }

        #endregion

        #region Helper Methods

        private DeviceBase FindChassisDevice()
        {
            return ChassisDevices.FirstOrDefault(d => d.DeviceType == "Chassis");
        }

        private DeviceBase FindParentDevice(DeviceBase childDevice)
        {
            foreach (var device in ChassisDevices)
            {
                if (device.Children?.Contains(childDevice) == true)
                    return device;
            }
            return null;
        }

        private bool IsChassisDevice(ProjectItem projectItem)
        {
            return projectItem.Type == "chassis_device";
        }

        private bool IsPxiCardDevice(ProjectItem projectItem)
        {
            return projectItem.Type.EndsWith("_card");
        }

        private string GetParentNodeName(ProjectItem projectItem)
        {
            return projectItem.Type switch
            {
                "controller_card" => "控制器",
                "switch_card" => "开关",
                "dmm_card" => "数字万用表",
                "power_supply" => "程控电源",
                "electronic_load" => "电子负载",
                "oscilloscope" => "示波器",
                _ => "设备"
            };
        }


        private void LoadChassisDevices(string chassisName)
        {
            try
            {
                ChassisDevices.Clear();
                var devices = _pxiChassisService.GetChassisDevices(chassisName);
                foreach (var device in devices)
                {
                    ChassisDevices.Add(device);
                }
            }
            catch (Exception)
            {
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
                // 使用 ResourceCleanupHelper 清理集合
                ResourceCleanupHelper.CleanupCollection(_chassisDevices);
                ResourceCleanupHelper.CleanupCollection(_tools);
                
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
