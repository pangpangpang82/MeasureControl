using MeasureControl.Constants;
using MeasureControl.Drivers.PXI3022;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.ViewModels.Hardware;
using MeasureControl.ViewModels.TestTask;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B;
using MeasureControl.ViewModels.Hardware;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using MeasureControl.Views;
using MeasureControl.Views.ConfigTabel;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.Hardware;
using MeasureControl.Views.TestTask;
using MeasureControl.Views.TestTask.CardCATPanel;
using MeasureControl.Views.TestTask.CardCATPanel.Mil1394B;
using MeasureControl.Views.TestTask.CardCATPanel.PXIe7131;
using OKAIPXIDevice;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeasureControl.ViewModels
{
    public class PxiChassisViewModel : BindableBase, INavigationAware, IDisposable
    {
        private const bool FixedDemoMode = true;
        private bool _isApplyingFixedDemoLayout;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IWindowManagerService _windowManagerService;
        private readonly IDialogService _dialogService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRegionManager _regionManager;
        private readonly ProjectService _projectService;
        private readonly Dictionary<string, System.Windows.FrameworkElement> _cachedSwitchPanels = new Dictionary<string, System.Windows.FrameworkElement>();
        private readonly Dictionary<string, System.Windows.FrameworkElement> _cachedInstrumentPanels = new Dictionary<string, System.Windows.FrameworkElement>();
        private MeasureControl.Views.TestTask.DmmTestPanelView _cachedDmmPanel;
        private MeasureControl.ViewModels.TestTask.DmmTestPanelViewModel _cachedDmmViewModel;
        private string _cachedDmmPanelKey;
        private readonly Dictionary<string, System.Windows.FrameworkElement> _cachedCardPanels = new Dictionary<string, System.Windows.FrameworkElement>();
        private readonly Dictionary<string, object> _cachedCardViewModels = new Dictionary<string, object>();
        private IRegionNavigationJournal _journal;
        private ObservableCollection<ProjectItem> _tools;
        private string _chassisName;
        private ObservableCollection<DeviceBase> _chassisDevices;
        private DeviceBase _selectedDevice;
        private DelegateCommand _openDeviceManualCommand;
        private ObservableCollection<string> _availableChassis;
        private string _selectedChassis;
        private bool _showDropHint;
        private ObservableCollection<DeviceInfoItem> _deviceInfoItems;
        private string _deviceInfoTitle = "暂无信息";
        private bool _disposed = false;
        private object _rightPanelContent;
        private string _chassisIpAddress;

        // TCP服务器管理相关字段
        private const int TcpBasePort2601 = 50200;
        private const int TcpBasePort3022 = 50300; // PXI3022使用不同的端口范围
        private const string LocalChassisIpAddress = "192.168.1.3";
        private const string RemoteClientIpAddress = "192.168.1.2";
        private readonly HashSet<string> _ownedTcpServerIdentifiers = new HashSet<string>();
        private readonly HashSet<int> _registeredLocalMatrixHandlerSlots = new HashSet<int>();

        public ObservableCollection<ProjectItem> Tools
        {
            get => _tools;
            set => SetProperty(ref _tools, value);
        }

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public ObservableCollection<DeviceBase> ChassisDevices
        {
            get => _chassisDevices;
            set => SetProperty(ref _chassisDevices, value);
        }

        public DeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    // 当选中的设备变化时，更新手册按钮的可用性
                    _openDeviceManualCommand?.RaiseCanExecuteChanged();
                    UpdateSelectedDeviceDisplayFields(value);
                    RaisePropertyChanged(nameof(SelectedDeviceManualInfo));
                }
            }
        }

        public string SelectedDeviceManualInfo
        {
            get
            {
                if (SelectedDevice == null) return string.Empty;
                if (!string.IsNullOrWhiteSpace(SelectedDevice.ManualUrl))
                {
                    return $"手册: {SelectedDevice.ManualUrl}";
                }
                var keys = new[] { SelectedDevice.CardName, SelectedDevice.Model, SelectedDevice.Name, SelectedDevice.DeviceType };
                foreach (var key in keys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (Helpers.ManualRegistry.TryGetManual(key, out var url) && !string.IsNullOrWhiteSpace(url))
                    {
                        var candidate = url;
                        if (!System.Uri.IsWellFormedUriString(url, System.UriKind.Absolute))
                        {
                            candidate = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, url.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            if (!System.IO.File.Exists(candidate))
                                candidate = url;
                        }
                        return $"手册: {candidate}";
                    }
                }
                return "未找到手册，检查 ManualRegistry 或 SelectedDevice.ManualUrl";
            }
        }

        public DelegateCommand OpenDeviceManualCommand =>
            _openDeviceManualCommand ??= new DelegateCommand(() =>
            {
                var device = SelectedDevice;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== OpenDeviceManual Debug ===");
                sb.AppendLine($"SelectedDevice != null: {device != null}");
                if (device != null)
                {
                    sb.AppendLine($"ManualUrl: '{device.ManualUrl ?? "<null>"}'");
                    sb.AppendLine($"CardName : '{device.CardName ?? "<null>"}'");
                    sb.AppendLine($"Model    : '{device.Model ?? "<null>"}'");
                    sb.AppendLine($"Name     : '{device.Name ?? "<null>"}'");
                    sb.AppendLine($"DeviceType: '{device.DeviceType ?? "<null>"}'");

                    var keys = new[] { device.CardName, device.Model, device.Name, device.DeviceType };
                    foreach (var k in keys)
                    {
                        var keyLabel = k ?? "<null>";
                        var registryUrl = MeasureControl.Helpers.ManualRegistry.GetManualUrl(k ?? string.Empty);
                        sb.AppendLine($"Registry lookup for '{keyLabel}' -> '{(string.IsNullOrWhiteSpace(registryUrl) ? "<no entry>" : registryUrl)}'");
                        if (!string.IsNullOrWhiteSpace(registryUrl) && !System.Uri.IsWellFormedUriString(registryUrl, System.UriKind.Absolute))
                        {
                            var candidate = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, registryUrl.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            sb.AppendLine($"  Resolved path: '{candidate}' Exists: {System.IO.File.Exists(candidate)}");
                        }
                    }
                }

                var url = device?.ManualUrl;
                if (string.IsNullOrWhiteSpace(url) && device != null)
                {
                    var keysToTry = new[] { device.CardName, device.Model, device.Name, device.DeviceType };
                    foreach (var k in keysToTry)
                    {
                        if (string.IsNullOrWhiteSpace(k)) continue;
                        var candidate = MeasureControl.Helpers.ManualRegistry.GetManualUrl(k);
                        if (!string.IsNullOrWhiteSpace(candidate))
                        {
                            url = candidate;
                            break;
                        }
                    }
                }
                sb.AppendLine($"Selected final url: '{url ?? "<null>"}'");

                try
                {
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        sb.AppendLine("No url found - aborting Process.Start.");
                        System.Diagnostics.Debug.WriteLine(sb.ToString());
                        return;
                    }

                    var resolved = url;
                    if (!System.Uri.IsWellFormedUriString(url, System.UriKind.Absolute))
                    {
                        var candidate = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, url.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(candidate))
                            resolved = candidate;
                        sb.AppendLine($"Resolved used: '{resolved}' (candidate exists: {System.IO.File.Exists(candidate)})");
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = resolved,
                        UseShellExecute = true
                    };
                    sb.AppendLine($"ProcessStartInfo: FileName='{psi.FileName}', UseShellExecute={psi.UseShellExecute}");
                    System.Diagnostics.Debug.WriteLine(sb.ToString());
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    var err = sb.ToString() + System.Environment.NewLine + "EXCEPTION: " + ex.ToString();
                    System.Diagnostics.Debug.WriteLine(err);
                    ReMessageBox.Show(err, "OpenDeviceManual Exception", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                // --- end debug helper ---
            }, () => HasManualAvailable(SelectedDevice));

        private bool HasManualAvailable(DeviceBase device)
        {
            if (device == null) return false;
            if (!string.IsNullOrWhiteSpace(device.ManualUrl)) return true;
            // check registry for CardName/Model/Name/DeviceType
            var keys = new[] { device.CardName, device.Model, device.Name, device.DeviceType };
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (Helpers.ManualRegistry.TryGetManual(key, out var url) && !string.IsNullOrWhiteSpace(url))
                    return true;
            }
            return false;
        }

        private void PromptReuseMatrixConnectionIfNeeded(DeviceBase nextDevice)
        {
            try
            {
                if (nextDevice == null) return;

                if (RightPanelContent is System.Windows.FrameworkElement oldElement)
                {
                    // 检查PXI2601_SWITCHViewModel或SwitchPXI3022ControlPanelViewModel
                    if (oldElement.DataContext is PXI2601_SWITCHViewModel oldSwitchVm1 && oldSwitchVm1.Device is SwitchDevice)
                    {
                        if (!oldSwitchVm1.IsDeviceConnected) return;
                        if (oldSwitchVm1.Device?.Id == nextDevice.Id) return;

                        var result = _dialogService?.ShowConfirmDialog(
                            "检测到上一次矩阵开关仍保持连接，是否复用上次矩阵连接（复用将不断开继电器）？",
                            "复用矩阵连接") ?? MessageBoxResult.No;

                        oldSwitchVm1.KeepMatrixConnectionOnClose = result == MessageBoxResult.Yes;
                    }
                    // 检查SwitchPXI3022ControlPanelViewModel
                    else if (oldElement.DataContext is SwitchPXI3022ControlPanelViewModel oldSwitchVm2 && oldSwitchVm2.Device is SwitchDevice)
                    {
                        if (!oldSwitchVm2.IsDeviceConnected) return;
                        if (oldSwitchVm2.Device?.Id == nextDevice.Id) return;

                        var result = _dialogService?.ShowConfirmDialog(
                            "检测到上一次矩阵开关仍保持连接，是否复用上次矩阵连接（复用将不断开继电器）？",
                            "复用矩阵连接") ?? MessageBoxResult.No;

                        oldSwitchVm2.KeepMatrixConnectionOnClose = result == MessageBoxResult.Yes;
                    }
                }
            }
            catch
            {
            }
        }

        public ObservableCollection<string> AvailableChassis
        {
            get => _availableChassis;
            set => SetProperty(ref _availableChassis, value);
        }

        public string SelectedChassis
        {
            get => _selectedChassis;
            set => SetProperty(ref _selectedChassis, value);
        }

        public bool ShowDropHint
        {
            get => _showDropHint;
            set => SetProperty(ref _showDropHint, value);
        }

        public ObservableCollection<DeviceInfoItem> DeviceInfoItems
        {
            get => _deviceInfoItems;
            set => SetProperty(ref _deviceInfoItems, value);
        }

        public string DeviceInfoTitle
        {
            get => _deviceInfoTitle;
            set => SetProperty(ref _deviceInfoTitle, value);
        }

        private System.Collections.ObjectModel.ObservableCollection<MeasureControl.Models.DeviceDisplayField> _selectedDeviceDisplayFields
            = new System.Collections.ObjectModel.ObservableCollection<MeasureControl.Models.DeviceDisplayField>();

        /// <summary>
        /// 供页面中绑定的、用于显示在简洁区域的设备字段集合（Label/Value/Format）。
        /// 当 SelectedDevice 变化时会更新为 SelectedDevice.GetDisplayFields() 的结果。
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<MeasureControl.Models.DeviceDisplayField> SelectedDeviceDisplayFields
        {
            get => _selectedDeviceDisplayFields;
            set => SetProperty(ref _selectedDeviceDisplayFields, value);
        }

        /// <summary>
        /// 右侧面板内容（用于导航）
        /// 切换时自动清理旧面板的 ViewModel
        /// </summary>
        public object RightPanelContent
        {
            get => _rightPanelContent;
            set
            {
                if (!ReferenceEquals(_rightPanelContent, value))
                {
                    // 清理旧面板的 ViewModel（若允许关闭）
                    if (_rightPanelContent is System.Windows.FrameworkElement oldElement)
                    {
                        if (oldElement.DataContext is ICloseGuard guard && !guard.CanClose())
                        {
                            return;
                        }

                        bool skipDispose = false;
                        try
                        {
                            if (oldElement.DataContext is PXI4004CANConfigPanelViewModel canVm && canVm.IsDeviceConnected)
                            {
                                skipDispose = true;
                                var chassis = string.IsNullOrWhiteSpace(canVm.ChassisName) ? ChassisName : canVm.ChassisName;
                                var devId = canVm.Device?.Id;
                                if (!string.IsNullOrWhiteSpace(chassis) && !string.IsNullOrWhiteSpace(devId))
                                {
                                    var key = $"{chassis}|{devId}|CAN";
                                    _cachedCardPanels[key] = oldElement;
                                    _cachedCardViewModels[key] = canVm;
                                }
                            }

                            // 检查PXI2601_SWITCHViewModel或SwitchPXI3022ControlPanelViewModel
                            PXI2601_SWITCHViewModel oldSwitchVm1 = null;
                            SwitchPXI3022ControlPanelViewModel oldSwitchVm2 = null;
                            bool isSwitchViewModel = false;

                            if (oldElement.DataContext is PXI2601_SWITCHViewModel vm1 && vm1.Device is SwitchDevice && vm1.IsDeviceConnected)
                            {
                                oldSwitchVm1 = vm1;
                                isSwitchViewModel = true;
                            }
                            else if (oldElement.DataContext is SwitchPXI3022ControlPanelViewModel vm2 && vm2.Device is SwitchDevice && vm2.IsDeviceConnected)
                            {
                                oldSwitchVm2 = vm2;
                                isSwitchViewModel = true;
                            }

                            if (isSwitchViewModel)
                            {
                                var result = _dialogService?.ShowConfirmDialog(
                                    "检测到上一次矩阵开关仍保持连接，是否复用上次矩阵连接（复用将不断开继电器）？",
                                    "复用矩阵连接") ?? MessageBoxResult.No;

                                if (oldSwitchVm1 != null)
                                {
                                    oldSwitchVm1.KeepMatrixConnectionOnClose = result == MessageBoxResult.Yes;
                                }
                                else if (oldSwitchVm2 != null)
                                {
                                    oldSwitchVm2.KeepMatrixConnectionOnClose = result == MessageBoxResult.Yes;
                                }

                                string deviceId = null;
                                bool keepConnection = false;

                                if (oldSwitchVm1 != null)
                                {
                                    deviceId = oldSwitchVm1.Device?.Id;
                                    keepConnection = oldSwitchVm1.KeepMatrixConnectionOnClose;
                                }
                                else if (oldSwitchVm2 != null)
                                {
                                    deviceId = oldSwitchVm2.Device?.Id;
                                    keepConnection = oldSwitchVm2.KeepMatrixConnectionOnClose;
                                }

                                if (keepConnection)
                                {
                                    skipDispose = true;
                                    if (!string.IsNullOrWhiteSpace(deviceId))
                                    {
                                        _cachedSwitchPanels[deviceId] = oldElement;
                                    }
                                }
                                else
                                {
                                    if (!string.IsNullOrWhiteSpace(deviceId))
                                    {
                                        _cachedSwitchPanels.Remove(deviceId);
                                    }
                                }
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.SignalGeneratorTestPanelViewModel sgVm &&
                                (sgVm.IsSignalGeneratorConnecting || sgVm.IsSignalGeneratorConnected))
                            {
                                skipDispose = true;
                                var key = sgVm.Device?.Id ?? "SignalGenerator";
                                _cachedInstrumentPanels[key] = oldElement;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.PowerSupplyTestPanelViewModel psVm &&
                                (psVm.IsPowerSupplyConnecting || psVm.IsPowerSupplyConnected))
                            {
                                skipDispose = true;
                                var key = psVm.Device?.Id ?? "PowerSupply";
                                _cachedInstrumentPanels[key] = oldElement;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.DmmTestPanelViewModel dmmVm &&
                                (dmmVm.IsDmmConnecting || dmmVm.IsDmmConnected))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.FrequencyCounterTestPanelViewModel fcVm &&
                                (fcVm.IsFrequencyCounterConnecting || fcVm.IsFrequencyCounterConnected))
                            {
                                skipDispose = true;
                                var key = "FrequencyCounter";
                                _cachedInstrumentPanels[key] = oldElement;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.PXIe7131_DIDOViewModel didoVm &&
                                (didoVm.IsBusy || didoVm.IsDeviceConnected || didoVm.IsOutputRunning))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.PXI7012_ROViewModel roVm &&
                                (roVm.IsBusy || roVm.IsDeviceConnected))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.MT532_AOViewModel aoVm &&
                                (aoVm.IsBusy || aoVm.IsDeviceConnected || aoVm.IsOutputRunning))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.PXI9774_AIViewModel aiVm &&
                                (aiVm.IsBusy || aiVm.IsDeviceConnected || aiVm.IsAcquisitionRunning))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.MTX970_LVDSViewModel lvdsVm &&
                                (lvdsVm.IsBusy || lvdsVm.IsConnected || lvdsVm.IsTesting))
                            {
                                skipDispose = true;
                            }

                            if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.ART4229ConfigPanelViewModel arinc429Vm &&
                                (arinc429Vm.IsConnected || arinc429Vm.IsSending || arinc429Vm.IsReceiving ||
                                 (arinc429Vm.Channels != null && arinc429Vm.Channels.Any(c => c?.CurrentConfig?.IsRunning == true))))
                            {
                                skipDispose = true;
                                var key = GetCardPanelCacheKey(arinc429Vm.Device, "ARINC429");
                                _cachedCardPanels[key] = oldElement;
                                _cachedCardViewModels[key] = arinc429Vm;
                            }

                            if (oldElement.DataContext is ART1553BConfigPanelViewModel mil1553Vm &&
                                (mil1553Vm.IsDeviceConnected || mil1553Vm.IsBCRunning || mil1553Vm.IsRTRunning || mil1553Vm.IsBMRunning))
                            {
                                skipDispose = true;
                                var key = GetCardPanelCacheKey(mil1553Vm.Device, "1553");
                                _cachedCardPanels[key] = oldElement;
                                _cachedCardViewModels[key] = mil1553Vm;
                            }

                            if (oldElement.DataContext is Mil1394TestPanelViewModel mil1394Vm && mil1394Vm.IsDeviceConnected)
                            {
                                skipDispose = true;
                                var key = GetCardPanelCacheKey(mil1394Vm.Device, "1394");
                                _cachedCardPanels[key] = oldElement;
                                _cachedCardViewModels[key] = mil1394Vm;
                            }

                            // 4087A(LVDT/RVDT) 与 4087C(Resolver) 切换时保留输入状态：缓存面板并跳过 Dispose
                            if (!skipDispose && oldElement.DataContext is MeasureControl.ViewModels.TestTask.LvdtSimulatorConfigPanelViewModel lvdtVm)
                            {
                                var key = $"{lvdtVm.ChassisName}|{lvdtVm.Device?.Id}|LVDT_SIM";
                                if (!string.IsNullOrWhiteSpace(lvdtVm.Device?.Id))
                                {
                                    _cachedCardPanels[key] = oldElement;
                                    _cachedCardViewModels[key] = lvdtVm;
                                }
                                skipDispose = true;
                            }

                            if (!skipDispose && oldElement.DataContext is MeasureControl.ViewModels.TestTask.ResolverSimulatorConfigPanelViewModel resolverVm)
                            {
                                var key = $"{resolverVm.ChassisName}|{resolverVm.Device?.Id}|RESOLVER_SIM";
                                if (!string.IsNullOrWhiteSpace(resolverVm.Device?.Id))
                                {
                                    _cachedCardPanels[key] = oldElement;
                                    _cachedCardViewModels[key] = resolverVm;
                                }
                                skipDispose = true;
                            }
                        }
                        catch
                        {
                        }

                        if (!skipDispose)
                        {
                            try
                            {
                                if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.SignalGeneratorTestPanelViewModel sgVm)
                                {
                                    var key = sgVm.Device?.Id ?? "SignalGenerator";
                                    _cachedInstrumentPanels.Remove(key);
                                }
                                else if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.PowerSupplyTestPanelViewModel psVm)
                                {
                                    var key = psVm.Device?.Id ?? "PowerSupply";
                                    _cachedInstrumentPanels.Remove(key);
                                }
                                else if (oldElement.DataContext is ART1553BConfigPanelViewModel mil1553Vm)
                                {
                                    var key = GetCardPanelCacheKey(mil1553Vm.Device, "1553");
                                    _cachedCardPanels.Remove(key);
                                    _cachedCardViewModels.Remove(key);
                                }
                                else if (oldElement.DataContext is Mil1394TestPanelViewModel mil1394Vm)
                                {
                                    var key = GetCardPanelCacheKey(mil1394Vm.Device, "1394");
                                    _cachedCardPanels.Remove(key);
                                    _cachedCardViewModels.Remove(key);
                                }
                                else if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.ART4229ConfigPanelViewModel arinc429Vm)
                                {
                                    var key = GetCardPanelCacheKey(arinc429Vm.Device, "ARINC429");
                                    _cachedCardPanels.Remove(key);
                                    _cachedCardViewModels.Remove(key);
                                }
                                else if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.DmmTestPanelViewModel)
                                {
                                    _cachedDmmPanel = null;
                                    _cachedDmmViewModel = null;
                                    _cachedDmmPanelKey = null;
                                }
                                else if (oldElement.DataContext is MeasureControl.ViewModels.TestTask.FrequencyCounterTestPanelViewModel)
                                {
                                    var key = "FrequencyCounter";
                                    _cachedInstrumentPanels.Remove(key);
                                }
                            }
                            catch
                            {
                            }

                            if (oldElement.DataContext is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                    }
                }
                SetProperty(ref _rightPanelContent, value);
            }
        }

        /// <summary>
        /// 机箱IP地址
        /// </summary>
        public string ChassisIpAddress
        {
            get => _chassisIpAddress;
            set => SetProperty(ref _chassisIpAddress, value);
        }

        public ICommand AddDeviceCommand { get; }
        public ICommand DeviceDoubleClickCommand { get; }
        public ICommand DeviceClickCommand { get; }
        public ICommand AddChassisCommand { get; }
        public ICommand ToggleDeviceExpansionCommand { get; }
        public ICommand SelectDeviceCommand { get; }
        public ICommand DeleteDeviceCommand { get; }
        public ICommand ClearDeviceSelectionCommand { get; }
        public ICommand ClearDeviceSelectionOnClickCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }
        public DelegateCommand<DeviceBase> RenameCardCommand { get; }

        public PxiChassisViewModel(IPxiChassisService pxiChassisService, IWindowManagerService windowManagerService,
            IDialogService dialogService, IEventAggregator eventAggregator, IRegionManager regionManager, ProjectService projectService)
        {
            _pxiChassisService = pxiChassisService;
            _windowManagerService = windowManagerService;
            _dialogService = dialogService;
            _eventAggregator = eventAggregator;
            _regionManager = regionManager;
            _projectService = projectService;
            Tools = new ObservableCollection<ProjectItem>();
            ChassisDevices = new ObservableCollection<DeviceBase>();
            AvailableChassis = new ObservableCollection<string>();
            DeviceInfoItems = new ObservableCollection<DeviceInfoItem>();
            ChassisName = "PXI机箱1";

            AddDeviceCommand = new DelegateCommand<ProjectItem>(OnAddDevice);
            DeviceDoubleClickCommand = new DelegateCommand<DeviceBase>(OnDeviceDoubleClick);
            DeviceClickCommand = new DelegateCommand<DeviceBase>(OnDeviceClick);
            AddChassisCommand = new DelegateCommand(OnAddChassis);
            ToggleDeviceExpansionCommand = new DelegateCommand<DeviceBase>(OnToggleDeviceExpansion);
            SelectDeviceCommand = new DelegateCommand<DeviceBase>(OnSelectDevice);
            DeleteDeviceCommand = new DelegateCommand<DeviceBase>(OnDeleteDevice);
            ClearDeviceSelectionCommand = new DelegateCommand(OnClearDeviceSelection);
            RenameCardCommand = new DelegateCommand<DeviceBase>(OnRenameCard);

            // 订阅设备修改事件（用于刷新全局通道编号显示）
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Subscribe(OnDeviceModified, ThreadOption.UIThread);
            ClearDeviceSelectionOnClickCommand = new DelegateCommand(OnClearDeviceSelection);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);

            // 订阅机箱选择事件
            _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Subscribe(OnPxiChassisSelected);

            // 订阅项目关闭事件
            _eventAggregator.GetEvent<ProjectClosedEvent>().Subscribe(OnProjectClosed);

            InitializeTools();
            InitializeAvailableChassis();
            InitializeChassis();
        }

        private void OnAddDevice(ProjectItem projectItem)
        {
            if (FixedDemoMode && !_isApplyingFixedDemoLayout)
            {
                return;
            }

            if (projectItem == null || string.IsNullOrEmpty(ChassisName)) return;
            if (projectItem.Children != null && projectItem.Children.Count > 0) return;

            bool isEmptySlotPlaceholder = projectItem.Name == "空槽" || projectItem.Name == "盲板";

            // 工具树上的“盲板”入口：实际添加到机箱中的设备仍为“空槽”，以复用现有占位逻辑
            string deviceNameToCreate = isEmptySlotPlaceholder ? "空槽" : projectItem.Name;

            // 直接创建具体的设备类实例
            DeviceBase device = DeviceFactory.CreateDevice(deviceNameToCreate, "");

            if (device == null)
            {
                _dialogService.ShowWarningDialog("无法识别的设备类型", "错误");
                return;
            }

            // 根据设备类型设置三段文字显示
            if (IsPxiCardDevice(projectItem))
            {
                // PXI板卡设备，需要添加到机箱下
                var chassisDevice = FindChassisDevice();
                if (chassisDevice == null)
                {
                    _dialogService.ShowWarningDialog("请先添加机箱设备", "提示");
                    return;
                }

                // 获取当前机箱配置
                var chassis = _pxiChassisService.GetChassisByName(ChassisName);

                // 控制器设备的特殊处理
                if (device is ControllerDevice)
                {
                    // 检查是否已经有控制器（一个机箱只能有一个控制器）
                    if (chassis != null && chassis.HasController())
                    {
                        if (FixedDemoMode && _isApplyingFixedDemoLayout)
                        {
                            return;
                        }

                        _dialogService.ShowWarningDialog("一个机箱只能有一个系统控制器！", "");
                        return;
                    }
                }
                else
                {
                    // 非控制器设备：必须先有控制器才能添加其他板卡
                    if (chassis != null && !chassis.HasController())
                    {
                        if (FixedDemoMode && _isApplyingFixedDemoLayout)
                        {
                            // 固定布局自动应用时不强制要求先添加控制器（例如机箱2仅矩阵开关场景）
                        }
                        else
                        {
                            _dialogService.ShowWarningDialog("请先添加系统控制器！", "");
                            return;
                        }
                    }
                }

                // 检查机箱的板卡数量限制（支持不同槽位数的机箱）
                if (chassisDevice is ChassisDevice chassisDeviceObj)
                {
                    // 计算已占用的槽位数（考虑多槽位设备）
                    int usedSlots = CalculateUsedSlots(chassisDeviceObj);

                    // 获取当前设备需要的槽位数
                    int requiredSlots = GetDeviceRequiredSlots(device);

                    // 检查是否有足够的槽位
                    if (usedSlots + requiredSlots > chassisDeviceObj.SlotCount)
                    {
                        _dialogService.ShowWarningDialog($"{chassisDeviceObj.SlotCount}槽机箱剩余槽位不足！当前已占用{usedSlots}个槽位，该设备需要{requiredSlots}个槽位。", "槽位不足");
                        return;
                    }
                }

                // 设置插槽位置为N/A（预留）
                device.SlotPosition = "N/A";

                if (isEmptySlotPlaceholder)
                {
                    // 占位符空槽：仅用于占用机箱槽位
                    // UI 子设备行：第一段显示 CardName/ParentNode，第二段显示 Model，第三段显示 SlotPosition
                    // 因此前两段置空，保持第三段由 UpdateAllSlotPositions 自动分配。
                    device.CardName = string.Empty;
                    device.ParentNode = string.Empty;
                    device.Model = string.Empty;
                    device.ConnectionMethod = "N/A";
                    device.DeviceType = "Card";
                    device.Status = "正常";
                    device.IsExpanded = false;
                }
                else
                {
                    // 板卡设备 - 三段文字：父节点类型名称、型号、插槽位置
                    device.ParentNode = GetParentNodeName(projectItem); // 父节点类型名称（如"控制器"）
                    device.ConnectionMethod = "N/A"; // 插槽位置预留为N/A
                    device.DeviceType = "Card"; // 设备类型
                    device.Status = "正常";
                    device.IsExpanded = false; // 板卡不需要展开（左侧不显示板卡的子节点）

                    // 板卡名称：同类型板卡使用“父节点类型名称+index”
                    var cardBaseName = device.ParentNode;
                    if (!string.IsNullOrWhiteSpace(cardBaseName))
                    {
                        if (FixedDemoMode &&
                            (string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal) ||
                             string.Equals(ChassisName, "PXI机箱2", StringComparison.Ordinal)))
                        {
                            device.CardName = cardBaseName;
                        }
                        else
                        {
                            int existingCount = 0;
                            try
                            {
                                existingCount = chassisDevice.Children
                                    .Where(d => d != null && d.DeviceType == "Card" && string.Equals(d.ParentNode, cardBaseName, StringComparison.Ordinal))
                                    .Count();
                            }
                            catch
                            {
                                existingCount = 0;
                            }

                            device.CardName = $"{cardBaseName}{existingCount + 1}";
                        }
                    }
                }

                // 使用ParentNode设置设备类型名称（对于通用设备、开关设备等）
                if (!string.IsNullOrEmpty(device.ParentNode))
                {
                    if (device is GenericDevice genericDevice)
                    {
                        genericDevice.SetDeviceTypeName(device.ParentNode);
                    }
                    else if (device is SwitchDevice switchDevice)
                    {
                        switchDevice.SetDeviceTypeName(device.ParentNode);
                        // NOTE: 不在这里启动 TCP，延后到设备加入 chassis.Children 并更新槽位之后启动，
                        // 否则 GetTcpListenPortForSwitchDevice 可能因 SlotIndex 尚未设置而回退到默认值。
                    }
                    else if (device is AnalogAcquisitionDevice analogAcqDevice)
                    {
                        analogAcqDevice.SetDeviceTypeName(device.ParentNode);
                    }
                    else if (device is ControllerDevice controllerDevice)
                    {
                        controllerDevice.SetDeviceTypeName(device.ParentNode);
                    }
                }

                // 对于控制器设备，需要插入到机箱的最前面
                if (device is ControllerDevice)
                {
                    // 控制器设备应该排在最前面，在所有其他板卡之前
                    if (chassisDevice.Children == null)
                        chassisDevice.Children = new ObservableCollection<DeviceBase>();
                    chassisDevice.Children.Insert(0, device);

                    // 更新所有板卡的槽位位置
                    if (chassisDevice is ChassisDevice chassis2)
                    {
                        UpdateAllSlotPositions(chassis2);
                    }

                    // 手动触发Children属性更改通知
                    var childrenList = chassisDevice.Children;
                    chassisDevice.Children = null;
                    chassisDevice.Children = childrenList;

                    // 所有设备都需要保存到服务中
                    _pxiChassisService.AddDeviceToChassis(ChassisName, device);

                    // 更新拖放提示显示状态
                    UpdateDropHintVisibility();

                    // 使用OnDeviceClick来选中设备并更新详细信息
                    OnDeviceClick(device);

                    // 发布设备修改事件
                    if (!_isApplyingFixedDemoLayout)
                    {
                        _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
                        {
                            ChassisName = ChassisName,
                            ModificationType = "Add",
                            DeviceInfo = $"{device.ParentNode} - {device.Model}"
                        });
                    }

                    return; // 提前返回，避免重复处理
                }

                // 确保子节点已初始化 - 在添加到机箱之前检查并初始化
                if (device.Children == null || device.Children.Count == 0)
                {
                    device.InitializeChildren();
                }
                // 对于模拟量采集设备，验证AnalogInputNode是否正确创建
                if (device is AnalogAcquisitionDevice analogDevice)
                {
                    if (analogDevice.AiNode != null)
                    {
                    }
                    else
                    {
                        analogDevice.InitializeChildren();
                    }
                }

                // 添加到机箱子设备列表
                if (chassisDevice.Children == null)
                    chassisDevice.Children = new ObservableCollection<DeviceBase>();
                chassisDevice.Children.Add(device);

                // 更新所有板卡的槽位位置（包括新添加的板卡）
                if (chassisDevice is ChassisDevice chassisDeviceForSlot)
                {
                    UpdateAllSlotPositions(chassisDeviceForSlot);
                }

                // 手动触发Children属性更改通知，确保UI立即更新
                // 通过重新设置Children属性来触发PropertyChanged事件
                var children = chassisDevice.Children;
                chassisDevice.Children = null;
                chassisDevice.Children = children;
            }
            else
            {
                // 同级别设备（程控电源、电子负载、程控仪器仪表等）
                device.ParentNode = GetParentNodeName(projectItem); // 父节点类型名称
                // 连接方式：默认 LAN；自定义 USB 设备使用 USB
                device.ConnectionMethod = "LAN";
                if (!string.IsNullOrWhiteSpace(projectItem?.Name))
                {
                    if (projectItem.Name.Contains("RS422") || projectItem.Name.Contains("RS232") || projectItem.Name.Contains("FPGA"))
                    {
                        device.ConnectionMethod = "USB";
                    }
                }
                device.DeviceType = "Instrument"; // 设备类型
                device.IsExpanded = false; // 默认折叠

                // 同级设备名称：默认使用“父节点类型名称+index”；
                // 但“其他自定义设备”使用其子节点名称（projectItem.Name）+index
                var displayBaseName = string.Equals(device.ParentNode, "其他自定义设备", StringComparison.Ordinal)
                    ? projectItem.Name
                    : device.ParentNode;

                if (!string.IsNullOrWhiteSpace(displayBaseName))
                {
                    if (FixedDemoMode && string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal))
                    {
                        device.DisplayName = displayBaseName;
                    }
                    else
                    {
                        int existingCount = 0;
                        try
                        {
                            existingCount = ChassisDevices
                                .Where(d => d != null && d.DeviceType == "Instrument" &&
                                            (string.Equals(d.DisplayName, displayBaseName, StringComparison.Ordinal) ||
                                             (!string.IsNullOrWhiteSpace(d.DisplayName) && d.DisplayName.StartsWith(displayBaseName, StringComparison.Ordinal)) ||
                                             (string.IsNullOrWhiteSpace(d.DisplayName) && string.Equals(d.ParentNode, displayBaseName, StringComparison.Ordinal))))
                                .Count();
                        }
                        catch
                        {
                            existingCount = 0;
                        }

                        device.DisplayName = $"{displayBaseName}{existingCount + 1}";
                    }
                }

                // 添加到设备列表
                ChassisDevices.Add(device);
            }

            // 所有设备都需要保存到服务中（用于项目保存）
            // 包括板卡设备和其他设备，确保所有拖进机箱的设备都能被保存
            _pxiChassisService.AddDeviceToChassis(ChassisName, device);

            // 更新拖放提示显示状态
            UpdateDropHintVisibility();

            // 使用OnDeviceClick来选中设备并更新详细信息，确保触发完整的UI更新流程
            // 这样可以确保rightinfo区域正确显示子节点信息（包括板卡的子节点）
            OnDeviceClick(device);

            // 发布设备修改事件，通知MainWindowViewModel标记项目为已修改
            if (!_isApplyingFixedDemoLayout)
            {
                _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
                {
                    ChassisName = ChassisName,
                    ModificationType = "Add",
                    DeviceInfo = $"{device.ParentNode} - {device.Model}"
                });
            }

            // 在设备已加入 chassis.Children 并完成槽位更新后再启动 Switch 设备对应的 TCP 服务器，
            // 确保 GetTcpListenPortForSwitchDevice 能读取到正确的 SlotIndex。
            try
            {
                if (device is SwitchDevice && IsLocalChassisByIp())
                {
                    //Write3022();
                    int port = GetTcpListenPortForSwitchDevice(device);
                    string boardIdentifier = $"PXI2601_{port}";
                    StartTcpServerForPort(port, boardIdentifier);
                    Debug.WriteLine($"[PxiChassisViewModel] (moved) 为SwitchDevice启动TCP服务器: Port={port}, Board={boardIdentifier}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PxiChassisViewModel] 延迟启动TCP服务器时异常: {ex.Message}");
            }

            // 本地机箱：为矩阵开关注册后台命令处理器（无需打开面板）
            RefreshLocalMatrixHandlers();
        }
        private string GetNextAvailableSlot()
        {
            var chassisDevice = FindChassisDevice();
            if (chassisDevice?.Children == null) return "Slot1";

            var usedSlots = chassisDevice.Children
                .Where(d => !string.IsNullOrEmpty(d.ConnectionMethod) && d.ConnectionMethod.StartsWith("Slot"))
                .Select(d => d.ConnectionMethod)
                .ToList();

            for (int i = 1; i <= 9; i++) // PXI机箱通常有9个槽位
            {
                var slot = $"Slot{i}";
                if (!usedSlots.Contains(slot))
                {
                    return slot;
                }
            }

            return "Slot1"; // 默认返回Slot1
        }

        private void OnDeviceDoubleClick(DeviceBase device)
        {
            if (device != null)
            {
                // 选中设备
                SelectedDevice = device;

                if (IsRsSerialModule(device))
                {
                    NavigateToRsSerialDebugPanel(device);
                    return;
                }

                // 判断是否为板卡（排除控制器和机箱）
                if (IsCardDevice(device))
                {
                    NavigateToCardConfigPanel(device);
                }
                else if (device.DeviceType == "Instrument" && !(device is ControllerDevice))
                {
                    NavigateToInstrumentTestPanel(device);
                }
            }
        }

        private bool IsRsSerialModule(DeviceBase device)
        {
            if (device == null) return false;
            var text = (device.DisplayName ?? string.Empty) + "|" + (device.Model ?? string.Empty) + "|" + (device.Name ?? string.Empty);
            return text.IndexOf("RS422", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("RS232", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void NavigateToRsSerialDebugPanel(DeviceBase device)
        {
            if (device == null) return;

            var rsType = "RS";
            var text = (device.DisplayName ?? string.Empty) + "|" + (device.Model ?? string.Empty) + "|" + (device.Name ?? string.Empty);
            if (text.IndexOf("RS422", StringComparison.OrdinalIgnoreCase) >= 0) rsType = "RS422";
            else if (text.IndexOf("RS232", StringComparison.OrdinalIgnoreCase) >= 0) rsType = "RS232";

            var key = $"RS_SERIAL|{device.Id}";
            if (_cachedInstrumentPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                RightPanelContent = cachedPanel;
                return;
            }

            var title = device.DisplayName;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = device.Model;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                title = $"{rsType} 串口调试";
            }

            var viewModel = new MeasureControl.ViewModels.TestTask.RsSerialDebugPanelViewModel(rsType, title);
            var panel = new MeasureControl.Views.TestTask.RsSerialDebugPanel { DataContext = viewModel };
            _cachedInstrumentPanels[key] = panel;
            RightPanelContent = panel;
        }

        private bool TryGetLastMatrixSwitchContext(out string testTaskName, out string configTableName, out string chassisName)
        {
            testTaskName = _projectService?.LastMatrixSwitchTestTaskName;
            configTableName = _projectService?.LastMatrixSwitchConfigTableName;
            chassisName = _projectService?.LastMatrixSwitchChassisName;

            if (string.IsNullOrWhiteSpace(chassisName))
            {
                chassisName = ChassisName;
            }

            if (!string.IsNullOrWhiteSpace(testTaskName) && !string.IsNullOrWhiteSpace(configTableName))
            {
                return true;
            }

            try
            {
                var all = MatrixSwitchConfigTableViewModel.GetAllMatrixSwitchTableItems();
                var anyKey = all?.Keys?.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k) && k.Contains("/"));
                if (!string.IsNullOrWhiteSpace(anyKey))
                {
                    var parts = anyKey.Split(new[] { '/' }, 2);
                    if (parts.Length == 2)
                    {
                        testTaskName = parts[0];
                        configTableName = parts[1];
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void NavigateToInstrumentTestPanel(DeviceBase device)
        {
            if (device == null) return;

            TryGetLastMatrixSwitchContext(out var testTaskName, out var configTableName, out var chassisName);

            if (device is DmmDevice)
            {
                var key = $"{chassisName}|{testTaskName}|{configTableName}|DMM";
                if (_cachedDmmPanel == null || _cachedDmmViewModel == null || !string.Equals(_cachedDmmPanelKey, key, StringComparison.Ordinal))
                {
                    _cachedDmmPanelKey = key;
                    _cachedDmmViewModel = new MeasureControl.ViewModels.TestTask.DmmTestPanelViewModel(testTaskName, configTableName, chassisName, _pxiChassisService);
                    _cachedDmmPanel = new MeasureControl.Views.TestTask.DmmTestPanelView { DataContext = _cachedDmmViewModel };
                }

                RightPanelContent = _cachedDmmPanel;
            }
            else if (device is FrequencyCounterDevice)
            {
                var key = "FrequencyCounter";
                if (_cachedInstrumentPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    if (cachedPanel.DataContext is MeasureControl.ViewModels.TestTask.FrequencyCounterTestPanelViewModel cachedVm &&
                        (cachedVm.IsFrequencyCounterConnecting || cachedVm.IsFrequencyCounterConnected))
                    {
                        RightPanelContent = cachedPanel;
                        return;
                    }

                    _cachedInstrumentPanels.Remove(key);
                }

                var viewModel = new MeasureControl.ViewModels.TestTask.FrequencyCounterTestPanelViewModel(testTaskName, configTableName, chassisName, _pxiChassisService);
                var panel = new MeasureControl.Views.TestTask.FrequencyCounterTestPanelView { DataContext = viewModel };
                _cachedInstrumentPanels[key] = panel;
                RightPanelContent = panel;
            }
            else if (device is OscilloscopeDevice)
            {
                var viewModel = new MeasureControl.ViewModels.TestTask.OscilloscopeTestPanelViewModel(testTaskName, configTableName, chassisName, _pxiChassisService);
                var panel = new MeasureControl.Views.TestTask.OscilloscopeTestPanelView { DataContext = viewModel };
                RightPanelContent = panel;
            }
            else if (device is SignalGeneratorDevice)
            {
                var key = ((SignalGeneratorDevice)device)?.Id ?? "SignalGenerator";
                if (_cachedInstrumentPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    if (cachedPanel.DataContext is MeasureControl.ViewModels.TestTask.SignalGeneratorTestPanelViewModel cachedVm &&
                        (cachedVm.IsSignalGeneratorConnecting || cachedVm.IsSignalGeneratorConnected))
                    {
                        RightPanelContent = cachedPanel;
                        return;
                    }

                    _cachedInstrumentPanels.Remove(key);
                }

                var viewModel = new MeasureControl.ViewModels.TestTask.SignalGeneratorTestPanelViewModel(testTaskName, configTableName, chassisName, (SignalGeneratorDevice)device, _pxiChassisService);
                var panel = new MeasureControl.Views.TestTask.SignalGeneratorTestPanelView { DataContext = viewModel };
                _cachedInstrumentPanels[key] = panel;
                RightPanelContent = panel;
            }
            else if (device is PowerSupplyDevice)
            {
                var key = ((PowerSupplyDevice)device)?.Id ?? "PowerSupply";
                if (_cachedInstrumentPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    if (cachedPanel.DataContext is MeasureControl.ViewModels.TestTask.PowerSupplyTestPanelViewModel cachedVm &&
                        (cachedVm.IsPowerSupplyConnecting || cachedVm.IsPowerSupplyConnected))
                    {
                        RightPanelContent = cachedPanel;
                        return;
                    }

                    _cachedInstrumentPanels.Remove(key);
                }

                var viewModel = new MeasureControl.ViewModels.TestTask.PowerSupplyTestPanelViewModel(testTaskName, configTableName, chassisName, (PowerSupplyDevice)device, _pxiChassisService);
                var panel = new MeasureControl.Views.TestTask.PowerSupplyTestPanelView { DataContext = viewModel };
                _cachedInstrumentPanels[key] = panel;
                RightPanelContent = panel;
            }
        }

        /// <summary>
        /// 判断设备是否为板卡（排除控制器和机箱）
        /// </summary>
        private bool IsCardDevice(DeviceBase device)
        {
            if (device == null) return false;

            // 排除空槽占位符
            if (device.Name == "空槽") return false;

            // 排除控制器
            if (device is ControllerDevice) return false;

            // 排除机箱
            if (device is ChassisDeviceBase) return false;

            // 判断是否为板卡类型（DeviceType == "Card" 或者是 PxiDeviceBase 的子类）
            return device.DeviceType == "Card" || device is PxiDeviceBase;
        }

        /// <summary>
        /// 处理设备单击事件，显示设备详细信息
        /// </summary>
        private void OnDeviceClick(DeviceBase device)
        {
            if (device != null)
            {
                // 使用BeginUIInteraction/EndUIInteraction包装UI交互操作，避免触发项目修改事件
                _pxiChassisService.BeginUIInteraction();

                try
                {
                    // 对于模拟量采集设备，在初始化前检查AnalogInputNode状态
                    if (device is AnalogAcquisitionDevice analogDevBefore)
                    {
                        if (analogDevBefore.AiNode != null)
                        {
                        }
                    }

                    // 选中设备
                    SelectedDevice = device;

                    if (device.Name == "空槽")
                    {
                        // 空槽占位符：不显示任何右侧信息
                        DeviceInfoTitle = "暂无信息";
                        DeviceInfoItems = new ObservableCollection<DeviceInfoItem>();
                        ChassisIpAddress = "";
                        RightPanelContent = null;
                        return;
                    }

                    // 如果是机箱设备，获取机箱IP地址
                    if (device.DeviceType == "Chassis")
                    {
                        var chassis = _pxiChassisService.GetChassisByName(ChassisName);
                        if (chassis != null)
                        {
                            ChassisIpAddress = chassis.IpAddress ?? "";
                        }
                        else
                        {
                            ChassisIpAddress = "";
                        }
                    }
                    else
                    {
                        ChassisIpAddress = "";
                    }

                    // 设置标题为"设备详细信息"
                    DeviceInfoTitle = "设备详细信息";

                    // 对于板卡设备，确保子节点已正确初始化
                    if (device.DeviceType == "Card")
                    {
                        System.Diagnostics.Debug.WriteLine($"[PxiChassis] OnDeviceClick init-check: Device={device?.Name ?? device?.CardName}, ChildrenCount={(device?.Children?.Count ?? 0)}");
                        bool needsInit = false;

                        // 检查Children集合
                        if (device.Children == null || device.Children.Count == 0)
                        {
                            needsInit = true;
                        }

                        // 对于模拟量采集设备，还需要检查AnalogInputNode
                        if (device is AnalogAcquisitionDevice analogDev)
                        {
                            if (analogDev.AiNode == null)
                            {
                                needsInit = true;
                            }
                        }

                        if (needsInit)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PxiChassis] OnDeviceClick initializing children for {device?.Name ?? device?.CardName}");
                            device.InitializeChildren();
                            System.Diagnostics.Debug.WriteLine($"[PxiChassis] OnDeviceClick after init: ChildrenCount={(device?.Children?.Count ?? 0)}");
                            // 对于模拟量采集设备，再次验证AnalogInputNode
                            if (device is AnalogAcquisitionDevice analogDev2)
                            {
                            }
                        }
                    }
                }
                finally
                {
                    // 确保始终恢复UI交互标志
                    _pxiChassisService.EndUIInteraction();
                }

                // 更新设备详细信息显示
                UpdateDeviceInfoItems(device);

                // 验证DeviceInfoItems是否正确填充
                for (int i = 0; i < DeviceInfoItems.Count; i++)
                {
                    var item = DeviceInfoItems[i];
                }

                // 导航到 DeviceInfoPanel
                NavigateToDeviceInfoPanel();

                // 发布设备点击事件，通知HardwareConfigViewModel显示设备详细信息
                // Ensure Description defaults to device name if empty so UI shows something meaningful
                if (string.IsNullOrWhiteSpace(device.Description))
                {
                    device.Description = device.Name;
                }

                _eventAggregator.GetEvent<DeviceClickedEvent>().Publish(new DeviceClickedEventArgs
                {
                    Device = device,
                    DeviceType = device.DeviceType,
                    DeviceName = device.Name,
                    Manufacturer = device.Manufacturer,
                    Model = device.Model,
                    Status = device.Status,
                    Description = device.Description,
                    ConnectionMethod = device.ConnectionMethod,
                    ParentNode = device.ParentNode,
                    Details = device.Details
                });
            }
            else
            {
            }
        }

        /// <summary>
        /// 更新设备详细信息显示
        /// </summary>
        private void UpdateDeviceInfoItems(DeviceBase device)
        {
            // 创建新的集合来存储设备信息项
            var newDeviceInfoItems = new ObservableCollection<DeviceInfoItem>();

            if (device == null)
            {
                DeviceInfoItems = newDeviceInfoItems; // 替换为空集合
                return;
            }

            if (device.Name == "空槽")
            {
                DeviceInfoItems = newDeviceInfoItems;
                return;
            }
            //// 如果点击的是机箱设备，不显示右侧信息（9槽机箱不应该在右侧信息区域显示）
            //if (device.DeviceType == "Chassis")
            //{
            //    DeviceInfoItems = newDeviceInfoItems; // 替换为空集合
            //    return;
            //}

            // 直接使用DeviceBase获取信息
            if (device != null)
            {
                // 对于模拟量采集设备，检查AnalogInputNode
                if (device is AnalogAcquisitionDevice analogDev)
                {
                    if (analogDev.AiNode != null)
                    {
                    }
                    else
                    {
                        for (int i = 0; i < (analogDev.Children?.Count ?? 0); i++)
                        {
                            var child = analogDev.Children[i];
                        }
                    }
                }

                // 使用设备类的GetDeviceInfoItems方法获取信息
                var deviceInfoItems = device.GetDeviceInfoItems();
                // 如果设备类返回了信息项，使用它们
                if (deviceInfoItems.Count > 0)
                {
                    // 设置第一行板卡信息的高度和机箱高度一样
                    // 设置第二行开始，板卡的子节点高度和deviceListBorder中板卡的高度一样
                    for (int i = 0; i < deviceInfoItems.Count; i++)
                    {
                        var item = deviceInfoItems[i];

                        // 确保第一个字段（CardType）与左侧列表的第一个字段保持一致
                        // 优先显示CardName，否则显示ParentNode
                        if (i == 0 && string.IsNullOrEmpty(item.CardType))
                        {
                            item.CardType = !string.IsNullOrWhiteSpace(device.CardName)
                                ? device.CardName
                                : device.ParentNode;
                        }

                        if (i == 0)
                        {
                            // 第一行板卡信息：高度和机箱高度一样（46px）
                            item.Height = "46";
                            item.DeviceType = device.DeviceType;
                        }
                        else
                        {
                            // 第二行开始：板卡的子节点高度和左侧板卡的高度一样（40px）
                            item.Height = "40";
                            item.DeviceType = device.DeviceType;
                        }

                        newDeviceInfoItems.Add(item);
                    }
                }
                else
                {
                    // 如果设备类没有返回信息项，使用回退逻辑
                    CreateFallbackDeviceInfo(device, newDeviceInfoItems);
                }
            }
            else
            {
                CreateFallbackDeviceInfo(device, newDeviceInfoItems);
            }

            // 替换整个集合，触发PropertyChanged事件，确保UI更新
            DeviceInfoItems = newDeviceInfoItems;
        }

        /// <summary>
        /// 更新 SelectedDeviceDisplayFields 为指定设备的显示字段集合
        /// </summary>
        private void UpdateSelectedDeviceDisplayFields(DeviceBase device)
        {
            if (device == null)
            {
                SelectedDeviceDisplayFields = new System.Collections.ObjectModel.ObservableCollection<MeasureControl.Models.DeviceDisplayField>();
                return;
            }

            if (device.Name == "空槽")
            {
                SelectedDeviceDisplayFields = new System.Collections.ObjectModel.ObservableCollection<MeasureControl.Models.DeviceDisplayField>();
                return;
            }

            SelectedDeviceDisplayFields = device.GetDisplayFields();
        }

        /// <summary>
        /// 创建回退设备信息（当设备类没有返回信息时使用）
        /// </summary>
        private void CreateFallbackDeviceInfo(DeviceBase device, ObservableCollection<DeviceInfoItem> targetCollection)
        {
            // 回退到原来的逻辑 - 确保板卡设备能显示信息
            // 第一个字段使用ParentNode，与左侧列表保持一致
            string firstField = device.ParentNode ?? GetCardType(device);
            var cardInfo = DeviceInfoItem.CreateCardInfo(
                firstField,  // 第一个字段：与左侧列表的第一个字段一致
                device.Model ?? "N/A",
                GetSlotPosition(device),
                device.Status ?? "正常"
            );

            // 设置第一行板卡信息的高度和机箱高度一样（46px）
            cardInfo.Height = "46";
            cardInfo.DeviceType = device.DeviceType;
            targetCollection.Add(cardInfo);

            // 调试输出
        }

        /// <summary>
        /// 获取设备的插槽位置
        /// </summary>
        private string GetSlotPosition(DeviceBase device)
        {
            if (device.DeviceType == "Chassis")
            {
                return "N/A"; // 机箱设备无插槽位置
            }
            else if (device.DeviceType == "Card" || device.DeviceType == "Controller")
            {
                // 板卡设备或控制器设备，根据在机箱中的位置确定插槽
                var chassisDevice = FindChassisDevice();
                if (chassisDevice?.Children != null)
                {
                    // 计算当前设备的起始槽位
                    int startSlot = 1;
                    foreach (var child in chassisDevice.Children)
                    {
                        if (child == device)
                        {
                            // 找到当前设备
                            if (device is ControllerDevice controller)
                            {
                                // 控制器占用多个槽位
                                int endSlot = startSlot + controller.SlotsOccupied - 1;
                                return $"Slot{startSlot}-{endSlot}";
                            }
                            else
                            {
                                // 普通板卡占用1个槽位
                                return $"Slot{startSlot}";
                            }
                        }

                        // 累加已占用的槽位数
                        if (child is ControllerDevice ctrl)
                        {
                            startSlot += ctrl.SlotsOccupied;
                        }
                        else if (child.DeviceType == "Card" || child.DeviceType == "Controller")
                        {
                            startSlot += 1; // 普通板卡占1个槽位
                        }
                    }
                }
                return device.ConnectionMethod ?? "N/A";
            }
            else
            {
                return "N/A"; // 程控设备无插槽位置
            }
        }

        /// <summary>
        /// 判断是否为模拟量采集设备
        /// </summary>
        private bool IsAnalogAcquisitionDevice(DeviceBase device)
        {
            if (device?.DeviceType != "Card") return false;

            string name = device.Name?.ToLower() ?? "";
            string model = device.Model?.ToLower() ?? "";

            return name.Contains("模拟") || name.Contains("analog") ||
                   model.Contains("模拟") || model.Contains("analog");
        }

        /// <summary>
        /// 获取板卡类型
        /// </summary>
        private string GetCardType(DeviceBase device)
        {
            if (device.DeviceType == "Chassis")
            {
                return "机箱"; // 机箱设备
            }
            else if (device.DeviceType == "Card")
            {
                // 优先使用设备的DeviceTypeName属性
                if (!string.IsNullOrEmpty(device.DeviceTypeName) && device.DeviceTypeName != "通用设备")
                {
                    return device.DeviceTypeName;
                }

                // 如果DeviceTypeName为空或为"通用设备"，则根据设备名称或型号判断板卡类型
                string name = device.Name?.ToLower() ?? "";
                string model = device.Model?.ToLower() ?? "";

                // 模拟量采集板卡
                if (name.Contains("模拟") || name.Contains("analog") ||
                    model.Contains("模拟") || model.Contains("analog"))
                {
                    return "模拟量采集";
                }
                // 数字量采集板卡
                else if (name.Contains("数字") || name.Contains("digital") ||
                         model.Contains("数字") || model.Contains("digital"))
                {
                    return "数字量采集";
                }
                // 矩阵开关板卡
                else if (name.Contains("矩阵") || name.Contains("matrix") ||
                         model.Contains("矩阵") || model.Contains("matrix"))
                {
                    return "矩阵开关";
                }
                // 控制器板卡
                else if (name.Contains("控制") || name.Contains("controller") ||
                         model.Contains("控制") || model.Contains("controller"))
                {
                    return "控制器";
                }
                // 信号发生器板卡
                else if (name.Contains("信号") || name.Contains("signal") ||
                         model.Contains("信号") || model.Contains("signal"))
                {
                    return "信号发生器";
                }
                // 示波器板卡
                else if (name.Contains("示波") || name.Contains("oscilloscope") ||
                         model.Contains("示波") || model.Contains("oscilloscope"))
                {
                    return "示波器";
                }
                // 默认返回设备名称作为板卡类型
                else
                {
                    return device.Name ?? "未知板卡";
                }
            }
            else if (device.DeviceType == "Instrument")
            {
                // 优先使用设备的DeviceTypeName属性
                if (!string.IsNullOrEmpty(device.DeviceTypeName) && device.DeviceTypeName != "通用设备")
                {
                    return device.DeviceTypeName;
                }

                // 如果DeviceTypeName为空或为"通用设备"，则根据设备名称或型号判断
                string name = device.Name?.ToLower() ?? "";
                string model = device.Model?.ToLower() ?? "";

                if (name.Contains("电源") || name.Contains("power") ||
                    model.Contains("电源") || model.Contains("power"))
                {
                    return "程控电源";
                }
                else if (name.Contains("示波器") || name.Contains("oscilloscope") ||
                         model.Contains("示波器") || model.Contains("oscilloscope"))
                {
                    return "示波器";
                }
                else if (name.Contains("电子负载") || name.Contains("electronic load") ||
                         model.Contains("电子负载") || model.Contains("electronic load"))
                {
                    return "电子负载";
                }
                else
                {
                    return "程控设备";
                }
            }
            else
            {
                return "未知设备";
            }
        }

        /// <summary>
        /// 测试DeviceInfoItem功能
        /// </summary>
        public void TestDeviceInfoItem()
        {
            // 创建测试设备
            var testDevice = new AnalogAcquisitionDevice("模拟量采集 PXIe-2722G2", "Slot1");
            testDevice.SetDeviceTypeName("模拟量采集");

            // 测试UpdateDeviceInfoItems方法
            UpdateDeviceInfoItems(testDevice);

            // 验证结果
            if (DeviceInfoItems.Count > 0)
            {
                var firstItem = DeviceInfoItems[0];
            }
        }

        /// <summary>
        /// 计算机箱中已占用的槽位数（考虑多槽位设备）
        /// </summary>
        private int CalculateUsedSlots(ChassisDevice chassis)
        {
            if (chassis?.Children == null) return 0;

            int totalUsedSlots = 0;
            foreach (var child in chassis.Children)
            {
                if (child is ControllerDevice controller)
                {
                    // 控制器占用多个槽位
                    totalUsedSlots += controller.SlotsOccupied;
                }
                else if (child.DeviceType == "Card" || child.DeviceType == "Controller")
                {
                    // 普通板卡占用1个槽位
                    totalUsedSlots += 1;
                }
            }

            return totalUsedSlots;
        }

        /// <summary>
        /// 获取设备需要的槽位数
        /// </summary>
        private int GetDeviceRequiredSlots(DeviceBase device)
        {
            if (device is ControllerDevice controller)
            {
                return controller.SlotsOccupied;
            }
            else if (device.DeviceType == "Card" || device.DeviceType == "Controller")
            {
                return 1; // 普通板卡占用1个槽位
            }
            return 0;
        }

        /// <summary>
        /// 更新所有板卡的槽位位置显示（包括 SlotPosition、SlotIndex 和 ConnectionMethod）
        /// </summary>
        private void UpdateAllSlotPositions(ChassisDevice chassis)
        {
            if (chassis?.Children == null) return;

            // 遍历所有子设备，更新其槽位信息
            // 控制器固定占用 Slot 1，其他板卡从 Slot 2 开始
            int currentSlot = 1;
            foreach (var child in chassis.Children)
            {
                if (child is ControllerDevice controller)
                {
                    if (controller.SlotsOccupied > 1)
                    {
                        int endSlot = currentSlot + controller.SlotsOccupied - 1;
                        child.SlotPosition = $"Slot {currentSlot}-{endSlot}";
                        child.ConnectionMethod = $"Slot{currentSlot}-{endSlot}";
                    }
                    else
                    {
                        child.SlotPosition = $"Slot {currentSlot}";
                        child.ConnectionMethod = $"Slot{currentSlot}";
                    }
                    // 控制器 SlotIndex 固定为 1（已在构造函数中设置）
                    currentSlot += controller.SlotsOccupied;
                }
                else if (child.DeviceType == "Card")
                {
                    child.SlotPosition = $"Slot {currentSlot}";
                    child.ConnectionMethod = $"Slot{currentSlot}";

                    // 设置 SlotIndex（用于驱动初始化）
                    if (child is PxiDeviceBase pxiDevice)
                    {
                        pxiDevice.SlotIndex = currentSlot;
                    }

                    currentSlot += 1;
                }
            }
        }

        private void InitializeTools()
        {
            Tools = new ObservableCollection<ProjectItem>();

            var PXI = new ProjectItem { Name = "PXI系统", Icon = "/Resources/Logo/chip_b.png" };
            Tools.Add(PXI);

            // 移除机箱节点 - 机箱只能在 HardwareConfig 页面通过拖拽添加
            // var pxichassis = new ProjectItem { Name = "机箱", Icon = "/Resources/Logo/chip_b.png" };
            // PXI.Children.Add(pxichassis);
            // pxichassis.Children.Add(new ProjectItem { Name = "简仪 PXIe-2722G2", Icon = "/Resources/Logo/chip_b.png" });
            // pxichassis.Children.Add(new ProjectItem { Name = "简仪 PXIe-2519G2", Icon = "/Resources/Logo/chip_b.png" });

            var controlor = new ProjectItem { Name = "控制器", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(controlor);
            controlor.Children.Add(new ProjectItem { Name = "凌华 PXIe-3987", Icon = "/Resources/Logo/chip_b.png" });

            var mat_switch = new ProjectItem { Name = "矩阵开关", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(mat_switch);
            mat_switch.Children.Add(new ProjectItem { Name = "欧开 PXI-3022", Icon = "/Resources/Logo/chip_b.png" });
            mat_switch.Children.Add(new ProjectItem { Name = "阿尔泰 PXI-2601", Icon = "/Resources/Logo/chip_b.png" });


            var discrete = new ProjectItem { Name = "离散量输入输出", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(discrete);
            discrete.Children.Add(new ProjectItem { Name = "简仪 PXIe-7131", Icon = "/Resources/Logo/chip_b.png" });


            var anq_in = new ProjectItem { Name = "模拟量采集", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(anq_in);
            anq_in.Children.Add(new ProjectItem { Name = "阿尔泰 PXIe-9774", Icon = "/Resources/Logo/chip_b.png" });


            var anq_out = new ProjectItem { Name = "模拟量输出", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(anq_out);
            anq_out.Children.Add(new ProjectItem { Name = "芒果树 MT-X532", Icon = "/Resources/Logo/chip_b.png" });


            var res_out = new ProjectItem { Name = "电阻输出", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(res_out);
            res_out.Children.Add(new ProjectItem { Name = "阿尔泰 PXI-7012", Icon = "/Resources/Logo/chip_b.png" });


            var LVDT_RVDT = new ProjectItem { Name = "LVDT/RVDT模拟", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(LVDT_RVDT);
            LVDT_RVDT.Children.Add(new ProjectItem { Name = "欧开 PXI-4087A", Icon = "/Resources/Logo/chip_b.png" });
            LVDT_RVDT.Children.Add(new ProjectItem { Name = "欧开 PXI-4087C", Icon = "/Resources/Logo/chip_b.png" });



            var CAN = new ProjectItem { Name = "CAN", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(CAN);
            CAN.Children.Add(new ProjectItem { Name = "阿尔泰 PXI-4004", Icon = "/Resources/Logo/chip_b.png" });


            var ARINC429 = new ProjectItem { Name = "ARINC429", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(ARINC429);
            ARINC429.Children.Add(new ProjectItem { Name = "阿尔泰 PXIe-4227", Icon = "/Resources/Logo/chip_b.png" });


            var _1553B = new ProjectItem { Name = "1553B", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(_1553B);
            _1553B.Children.Add(new ProjectItem { Name = "阿尔泰 PXI-4332", Icon = "/Resources/Logo/chip_b.png" });


            var _1394B = new ProjectItem { Name = "1394B", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(_1394B);
            _1394B.Children.Add(new ProjectItem { Name = "怀智 HZ-MIL1394B-PX1e-4N", Icon = "/Resources/Logo/chip_b.png" });


            var LVDS = new ProjectItem { Name = "LVDS", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(LVDS);
            LVDS.Children.Add(new ProjectItem { Name = "芒果树 MT-X970", Icon = "/Resources/Logo/chip_b.png" });

            var blindPanel = new ProjectItem { Name = "盲板", Icon = "/Resources/Logo/chip_b.png" };
            PXI.Children.Add(blindPanel);

            var power = new ProjectItem { Name = "程控电源", Icon = "/Resources/Logo/power.png" };
            Tools.Add(power);
            power.Children.Add(new ProjectItem { Name = "艾德克斯 IT-N6332B", Icon = "/Resources/Logo/power.png" });
            power.Children.Add(new ProjectItem { Name = "艾德克斯 IT-M3912D-500-72", Icon = "/Resources/Logo/power.png" });
            //power.Children.Add(new ProjectItem { Name = "均测 DPM8605-485", Icon = "/Resources/Logo/power.png" });

            var elect_load = new ProjectItem { Name = "电子负载", Icon = "/Resources/Logo/signal.png" };
            Tools.Add(elect_load);
            //var sp = new ProjectItem { Name = "串口", Icon = "/Resources/Logo/signal.png" };
            //elect_load.Children.Add(elect_load);
            elect_load.Children.Add(new ProjectItem { Name = "Chroma 6314A", Icon = "/Resources/Logo/signal.png" });


            var instrument = new ProjectItem { Name = "程控仪器仪表", Icon = "/Resources/Logo/instrument.png" };
            Tools.Add(instrument);
            var dim = new ProjectItem { Name = "数字多用表", Icon = "/Resources/Logo/instrument.png" };
            instrument.Children.Add(dim);
            dim.Children.Add(new ProjectItem { Name = "普源 DM3068", Icon = "/Resources/Logo/instrument.png" });

            var sig = new ProjectItem { Name = "信号发生器", Icon = "/Resources/Logo/instrument.png" };
            instrument.Children.Add(sig);
            sig.Children.Add(new ProjectItem { Name = "普源 DG1032Z", Icon = "/Resources/Logo/instrument.png" });

            var oscilloscope = new ProjectItem { Name = "示波器", Icon = "/Resources/Logo/instrument.png" };
            instrument.Children.Add(oscilloscope);
            oscilloscope.Children.Add(new ProjectItem { Name = "普源 DH04804", Icon = "/Resources/Logo/instrument.png" });
            oscilloscope.Children.Add(new ProjectItem { Name = "DS01104", Icon = "/Resources/Logo/instrument.png" });

            var frem = new ProjectItem { Name = "频率计", Icon = "/Resources/Logo/instrument.png" };
            instrument.Children.Add(frem);
            frem.Children.Add(new ProjectItem { Name = "是德 53220A", Icon = "/Resources/Logo/instrument.png" });

            var other = new ProjectItem { Name = "其他自定义设备", Icon = "/Resources/Logo/folder.png" };
            Tools.Add(other);
            other.Children.Add(new ProjectItem { Name = "RS422模块", Icon = "/Resources/Logo/folder.png" });
            other.Children.Add(new ProjectItem { Name = "RS232模块", Icon = "/Resources/Logo/folder.png" });
            other.Children.Add(new ProjectItem { Name = "FPGA_高速_IO板", Icon = "/Resources/Logo/folder.png" });
        }

        private void LoadChassisDevices()
        {
            if (string.IsNullOrEmpty(ChassisName))
            {
                ChassisDevices.Clear();
                UpdateDropHintVisibility();
                return;
            }

            try
            {
                // 清空现有设备，包括机箱设备的子设备
                ChassisDevices.Clear();

                var devices = _pxiChassisService.GetChassisDevices(ChassisName);

                // 首先添加机箱设备（如果存在）
                var chassisDevice = devices.FirstOrDefault(d => d.DeviceType == "Chassis");
                if (chassisDevice != null)
                {
                    // 确保机箱设备的Children集合已初始化
                    if (chassisDevice.Children == null)
                        chassisDevice.Children = new ObservableCollection<DeviceBase>();
                    // 注意：不要清空Children集合，因为板卡设备需要保留在机箱的子设备中

                    ChassisDevices.Add(chassisDevice);
                }

                // 然后添加其他设备（非机箱设备）
                foreach (var device in devices)
                {
                    // 跳过机箱设备和板卡设备（板卡设备已经在机箱的Children中）
                    if (device.DeviceType == "Chassis" || device.DeviceType == "Card")
                        continue;

                    // 其他设备：直接添加到ChassisDevices集合
                    ChassisDevices.Add(device);
                }

                // 加载完成后，更新所有板卡的槽位信息
                if (chassisDevice is ChassisDevice chassisDeviceForSlot)
                {
                    UpdateAllSlotPositions(chassisDeviceForSlot);
                }

                // 本地机箱：为矩阵开关注册后台命令处理器（无需打开面板）
                RefreshLocalMatrixHandlers();

                // 更新拖放提示显示状态
                UpdateDropHintVisibility();
            }
            catch (Exception)
            {
                ChassisDevices.Clear();
                UpdateDropHintVisibility();
            }
        }

        private void InitializeAvailableChassis()
        {
            AvailableChassis.Clear();
            AvailableChassis.Add("简仪 PXIe-2722G2");
            AvailableChassis.Add("简仪 PXIe-2519G2");
        }

        private void InitializeChassis()
        {
            // 确保在服务中创建对应的机箱
            var chassis = _pxiChassisService.GetChassisByName(ChassisName);
            if (chassis == null)
            {
                // 创建新的机箱
                var newChassis = new ChassisModel(ChassisName, 0, 0);
                _pxiChassisService.AddChassis(newChassis);
            }
        }

        private void OnAddChassis()
        {
            if (string.IsNullOrEmpty(SelectedChassis))
            {
                _dialogService.ShowWarningDialog("请先选择一个机箱", "提示");
                return;
            }

            // 检查是否已经存在机箱
            if (FindChassisDevice() != null)
            {
                _dialogService.ShowWarningDialog("一个PXI机箱只能添加一个机箱设备", "提示");
                return;
            }

            try
            {
                // 创建新机箱设备实例
                // var chassis = new ChassisDevice(SelectedChassis);
                var chassis = DeviceFactory.CreateDevice(SelectedChassis);
                // ParentNode 会在 ChassisDevice 构造函数中自动设置为 DeviceTypeName（如"18槽机箱"或"9槽机箱"）
                chassis.Model = SelectedChassis;
                chassis.ConnectionMethod = "详细信息";
                chassis.Details = "详细信息"; // 机箱设备的详细信息
                chassis.DeviceType = "Chassis";
                chassis.Status = "正常";
                chassis.IsExpanded = true;

                // 确保机箱有子设备集合
                if (chassis.Children == null)
                    chassis.Children = new ObservableCollection<DeviceBase>();

                // 添加到设备列表的顶部
                ChassisDevices.Insert(0, chassis);
                SelectedDevice = chassis;

                // 更新拖放提示显示状态
                UpdateDropHintVisibility();

                _dialogService.ShowInfoDialog($"成功添加机箱: {SelectedChassis}", "成功");
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"添加机箱时发生错误: {ex.Message}", "错误");
            }
        }

        private void OnToggleDeviceExpansion(DeviceBase device)
        {
            if (device != null)
            {
                // 对于所有板卡（排除控制器和机箱），双击时打开配置面板
                if (IsCardDevice(device))
                {
                    OnDeviceDoubleClick(device);
                }
                else
                {
                    // 其他设备（控制器、机箱等）保持展开/折叠功能
                    device.IsExpanded = !device.IsExpanded;
                }
            }
        }

        private void OnSelectDevice(DeviceBase device)
        {
            if (device != null)
            {
                // 调用 OnDeviceClick 来更新设备详细信息
                OnDeviceClick(device);
            }
        }

        private void OnClearDeviceSelection()
        {
            SelectedDevice = null;
            DeviceInfoTitle = "暂无信息";
            DeviceInfoItems.Clear();
            RightPanelContent = null;
        }

        /// <summary>
        /// 导航到设备信息面板
        /// </summary>
        private void NavigateToDeviceInfoPanel()
        {
            var deviceInfoViewModel = new DeviceInfoViewModel(DeviceInfoItems);
            var deviceInfoPanel = new DeviceInfoPanel
            {
                DataContext = deviceInfoViewModel
            };
            RightPanelContent = deviceInfoPanel;
        }

        /// <summary>
        /// 导航到模拟量通道/输出配置面板
        /// 模拟量采集板卡 -> AnalogInputConfigPanel
        /// 模拟量输出板卡 -> AnalogOutputConfigPanel
        /// </summary>
        public void NavigateToAnalogInputConfigPanel(DeviceBase device)
        {
            if (device == null)
            {
                return;
            }

            // 使用服务中的权威设备引用（避免项目加载/类型修复过程中对象实例被替换导致首次打开读到旧实例）
            var authoritativeDevice = _pxiChassisService?.GetDeviceById(device.Id);
            if (authoritativeDevice != null)
            {
                device = authoritativeDevice;
            }

            if (device is AnalogAcquisitionDevice)
            {
                var key = $"{ChassisName}|{device.Id}|AI";
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    RightPanelContent = cachedPanel;
                    return;
                }

                var viewModel = new PXI9774_AIViewModel(device, ChassisName, _pxiChassisService, _eventAggregator, null, _projectService);
                var panel = new PXI9774_AI { DataContext = viewModel };
                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
            }
            else if (device is AnalogOutputDevice)
            {
                // 传入 projectService 以便任务列表正常加载
                var key = $"{ChassisName}|{device.Id}|AO";
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    RightPanelContent = cachedPanel;
                    return;
                }

                var viewModel = new MT532_AOViewModel(device, ChassisName, _pxiChassisService, _eventAggregator, _projectService);
                var panel = new MT532_AO { DataContext = viewModel };
                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
            }
        }

        /// <summary>
        /// 导航到数字量IO配置面板（离散量输入输出）
        /// </summary>
        public void NavigateToDigitalIOConfigPanel(DeviceBase device)
        {
            var key = $"{ChassisName}|{device.Id}|DIDO";
            if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                RightPanelContent = cachedPanel;
                return;
            }

            var viewModel = new PXIe7131_DIDOViewModel(device, ChassisName, _pxiChassisService, _eventAggregator, _projectService);
            var panel = new PXIe7131_DIDO { DataContext = viewModel };
            _cachedCardPanels[key] = panel;
            _cachedCardViewModels[key] = viewModel;
            RightPanelContent = panel;
        }


        public void NavigateToSwitchConfigPanel(DeviceBase device)
        {
            if (device == null)
            {
                if (SelectedDevice is SwitchDevice)
                {
                    device = SelectedDevice;
                }
                else
                {
                    try
                    {
                        var chassis = _pxiChassisService?.GetChassisByName(ChassisName);
                        var chassisDevice = chassis?.Devices?.FirstOrDefault(d => d.DeviceType == AppConstants.DeviceTypeChassis);
                        device = chassisDevice?.Children?.OfType<SwitchDevice>()?.FirstOrDefault();
                    }
                    catch
                    {
                    }
                }
            }

            if (device == null)
            {
                System.Diagnostics.Debug.WriteLine($"[NavigateToSwitchConfigPanel] device为空，无法打开矩阵开关配置面板。ChassisName={ChassisName}");
                _dialogService?.ShowWarningDialog("未找到矩阵开关板卡，请先添加或重新选择板卡。", "提示");
                return;
            }

            if (!string.IsNullOrWhiteSpace(device.Id) && _cachedSwitchPanels.TryGetValue(device.Id, out var cachedPanel) && cachedPanel != null)
            {
                // 即使使用缓存的面板，也需要确保ViewModel使用最新的设备配置
                if (cachedPanel.DataContext is PXI2601_SWITCHViewModel cachedVm)
                {
                    // 更新ViewModel中的Device引用为最新的设备实例
                    var latestDevice = _pxiChassisService?.GetDeviceById(device.Id);
                    if (latestDevice != null && latestDevice != cachedVm.Device)
                    {
                        cachedVm.Device = latestDevice;
                        // 重新加载配置以确保显示最新的连接状态
                        cachedVm.LoadDeviceConfig();
                    }
                }
                RightPanelContent = cachedPanel;
                return;
            }

            // Decide which switch panel to open based on device model
            var pxiDevice = device as PxiDeviceBase;
            string modelLower = (pxiDevice?.Model ?? device?.Model ?? string.Empty).ToLowerInvariant();

            if (modelLower.Contains("3022") || modelLower.Contains("pxi3022") || modelLower.Contains("pxi-3022"))
            {
                // PXI3022-specific control panel
                var viewModel3022 = new SwitchPXI3022ControlPanelViewModel(device, ChassisName, _pxiChassisService, _eventAggregator);
                var panel3022 = new Views.TestTask.SwitchPXI3022ConfigPanel { DataContext = viewModel3022 };
                RightPanelContent = panel3022;
            }
            else
            {
                // Default to PXI2601 panel for PXI-2601 or other switch devices
                var viewModel2601 = new PXI2601_SWITCHViewModel(device, ChassisName, _pxiChassisService, _eventAggregator);
                var panel2601 = new PXI2601_SWITCH { DataContext = viewModel2601 };
                RightPanelContent = panel2601;
            }
        }
        /// <summary>
        /// 导航到CAN总线配置面板
        /// </summary>
        public void NavigateToCanBusConfigPanel(DeviceBase device)
        {
            if (device == null)
            {
                return;
            }

            var authoritativeDevice = _pxiChassisService?.GetDeviceById(device.Id);
            if (authoritativeDevice != null)
            {
                device = authoritativeDevice;
            }

            var key = $"{ChassisName}|{device.Id}|CAN";
            if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                if (cachedPanel.DataContext is PXI4004CANConfigPanelViewModel cachedVm && cachedVm.IsDeviceConnected)
                {
                    RightPanelContent = cachedPanel;
                    return;
                }

                _cachedCardPanels.Remove(key);
                _cachedCardViewModels.Remove(key);
            }

            var viewModel = new PXI4004CANConfigPanelViewModel(device, ChassisName, _pxiChassisService, _eventAggregator);
            var panel = new Views.PXI4004CANConfigPanel { DataContext = viewModel };
            _cachedCardPanels[key] = panel;
            _cachedCardViewModels[key] = viewModel;
            RightPanelContent = panel;
        }

        /// <summary>
        /// 导航到ARINC429配置面板
        /// </summary>

        public void NavigateToArinc429ConfigPanel(DeviceBase device)
        {
            if (device == null)
            {
                return;
            }

            Debug.WriteLine($"[PxiChassis] NavigateToArinc429ConfigPanel: DeviceType={device.GetType().Name}, Name={device.Name}, Model={device.Model}, CardName={device.CardName}, Id={device.Id}");

            var key = GetCardPanelCacheKey(device, "ARINC429");
            if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                RightPanelContent = cachedPanel;
                return;
            }

            var viewModel = new MeasureControl.ViewModels.TestTask.ART4229ConfigPanelViewModel(
                device,
                ChassisName,
                _pxiChassisService,
                _eventAggregator);

            var panel = new MeasureControl.Views.TestTask.ART4229ConfigPanel
            {
                DataContext = viewModel
            };

            _cachedCardPanels[key] = panel;
            _cachedCardViewModels[key] = viewModel;
            RightPanelContent = panel;

            Debug.WriteLine($"[PxiChassis] RightPanelContent set to {panel.GetType().Name}, DataContext={viewModel.GetType().Name}");
        }

        private string GetCardPanelCacheKey(DeviceBase device, string panelType)
        {
            var id = device?.Id;
            if (!string.IsNullOrWhiteSpace(id))
            {
                return $"{ChassisName}|{id}|{panelType}";
            }

            if (device is PxiDeviceBase pxi && pxi.SlotIndex > 0)
            {
                return $"{ChassisName}|SlotIndex:{pxi.SlotIndex}|{panelType}";
            }

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot) && !string.Equals(slot, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                return $"{ChassisName}|SlotPosition:{slot}|{panelType}";
            }

            var name = device?.Name;
            var model = device?.Model;
            return $"{ChassisName}|Name:{name}|Model:{model}|{panelType}";
        }

        /// <summary>
        /// 导航到MIL-1553B配置面板
        /// </summary>
        public void NavigateToMil1553BConfigPanel(DeviceBase device)
        {
            Debug.WriteLine($"[PxiChassis] NavigateToMil1553BConfigPanel: DeviceType={device?.GetType().Name}, Name={device?.Name}, CardName={device?.CardName}, ChildrenCount={(device?.Children?.Count ?? 0)}");
            if (device != null && (device.Children == null || device.Children.Count == 0))
            {
                Debug.WriteLine("[PxiChassis] NavigateToMil1553BConfigPanel: Children missing, calling InitializeChildren()");
                device.InitializeChildren();
                Debug.WriteLine($"[PxiChassis] NavigateToMil1553BConfigPanel: After init ChildrenCount={(device?.Children?.Count ?? 0)}");
            }
            var key = GetCardPanelCacheKey(device, "1553");
            if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                RightPanelContent = cachedPanel;
                return;
            }

            var viewModel = new ViewModels.TestTask.CardCATPanel.ART1553BConfigPanelViewModel(device, ChassisName, _pxiChassisService, _eventAggregator, _projectService);
            var panel = new Views.TestTask.CardCATPanel.ART1553BConfigPanel { DataContext = viewModel };
            _cachedCardPanels[key] = panel;
            _cachedCardViewModels[key] = viewModel;
            RightPanelContent = panel;
            Debug.WriteLine($"[PxiChassis] RightPanelContent set to {panel.GetType().Name}, DataContext={viewModel?.GetType().Name}");
        }
        /// <summary>
        /// 导航到LVDT模拟器配置面板
        /// </summary>
        public void NavigateToLvdtSimulatorConfigPanel(DeviceBase device)
        {
            if (device is LvdtSimulatorDevice lvdtDevice)
            {
                // 使用服务中的权威设备引用（避免项目加载/类型修复过程中对象实例被替换导致复用缓存时引用旧实例）
                var authoritativeDevice = _pxiChassisService?.GetDeviceById(lvdtDevice.Id) as LvdtSimulatorDevice;
                if (authoritativeDevice != null)
                {
                    lvdtDevice = authoritativeDevice;
                }

                var key = $"{ChassisName}|{lvdtDevice.Id}|LVDT_SIM";
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    // 复用缓存面板以保留上次输入状态
                    if (cachedPanel.DataContext is MeasureControl.ViewModels.TestTask.LvdtSimulatorConfigPanelViewModel cachedVm)
                    {
                        cachedVm.Device = lvdtDevice;
                        cachedVm.ChassisName = ChassisName;
                    }
                    RightPanelContent = cachedPanel;
                    return;
                }

                var viewModel = new ViewModels.TestTask.LvdtSimulatorConfigPanelViewModel(
                    lvdtDevice,
                    ChassisName,
                    _pxiChassisService,
                    _eventAggregator,
                    _projectService);
                var panel = new Views.TestTask.LvdtSimulatorConfigPanel { DataContext = viewModel };

                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
            }
        }

        /// <summary>
        /// 导航到 Resolver（旋变）配置面板
        /// </summary>
        public void NavigateToResolverSimulatorConfigPanel(DeviceBase device)
        {
            if (device is ResolverSimulatorDevice resolverDevice)
            {
                // 使用服务中的权威设备引用（避免项目加载/类型修复过程中对象实例被替换导致复用缓存时引用旧实例）
                var authoritativeDevice = _pxiChassisService?.GetDeviceById(resolverDevice.Id) as ResolverSimulatorDevice;
                if (authoritativeDevice != null)
                {
                    resolverDevice = authoritativeDevice;
                }

                var key = $"{ChassisName}|{resolverDevice.Id}|RESOLVER_SIM";
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    // 复用缓存面板以保留上次输入状态
                    if (cachedPanel.DataContext is MeasureControl.ViewModels.TestTask.ResolverSimulatorConfigPanelViewModel cachedVm)
                    {
                        cachedVm.Device = resolverDevice;
                        cachedVm.ChassisName = ChassisName;
                    }
                    RightPanelContent = cachedPanel;
                    return;
                }

                Debug.WriteLine($"NavigateToResolverSimulatorConfigPanel called for device: {resolverDevice.Name}, Model:{resolverDevice.Model}, CardName:{resolverDevice.CardName}");
                var viewModel = new ViewModels.TestTask.ResolverSimulatorConfigPanelViewModel(
                    device: resolverDevice,
                    chassisName: ChassisName,
                    pxiChassisService: _pxiChassisService,
                    eventAggregator: _eventAggregator,
                    projectService: _projectService);

                var panel = new Views.TestTask.ResolverSimulatorConfigPanel { DataContext = viewModel };

                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
                Debug.WriteLine("RightPanelContent set to ResolverSimulatorConfigPanel, DataContext DeviceName=" + viewModel.DeviceName);
            }
        }


        /// <summary>
        /// 导航到MIL-1394B配置面板
        /// </summary>
        public void NavigateToMil1394BConfigPanel(DeviceBase device)
        {
            if (device is Mil1394BDevice mil1394Device)
            {
                var key = GetCardPanelCacheKey(device, "1394");
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    RightPanelContent = cachedPanel;
                    return;
                }

                var viewModel = new Mil1394TestPanelViewModel(mil1394Device, ChassisName, _pxiChassisService, _eventAggregator, _projectService);
                var panel = new Mil1394TestPanel { DataContext = viewModel };
                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
            }
        }

        /// <summary>
        /// 导航到LVDS配置面板
        /// </summary>
        public void NavigateToLvdsConfigPanel(DeviceBase device)
        {
            if (device is LvdsDevice lvdsDevice)
            {
                var key = $"{ChassisName}|{device.Id}|LVDS";
                if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
                {
                    RightPanelContent = cachedPanel;
                    return;
                }

                var viewModel = new MTX970_LVDSViewModel(lvdsDevice, ChassisName, _projectService);
                var panel = new Views.TestTask.CardCATPanel.MTX970_LVDS { DataContext = viewModel };
                _cachedCardPanels[key] = panel;
                _cachedCardViewModels[key] = viewModel;
                RightPanelContent = panel;
            }
        }

        /// <summary>
        /// 导航到程控电阻配置面板
        /// </summary>
        public void NavigateToProgrammableResistorConfigPanel(DeviceBase device)
        {
            if (device == null)
            {
                return;
            }

            var key = $"{ChassisName}|{device.Id}|RO";
            if (_cachedCardPanels.TryGetValue(key, out var cachedPanel) && cachedPanel != null)
            {
                RightPanelContent = cachedPanel;
                return;
            }

            var viewModel = new PXI7012_ROViewModel(device, ChassisName, _pxiChassisService, _eventAggregator, _projectService);
            var panel = new PXI7012_RO { DataContext = viewModel };
            _cachedCardPanels[key] = panel;
            _cachedCardViewModels[key] = viewModel;
            RightPanelContent = panel;
        }

        /// <summary>
        /// 根据设备类型导航到对应的配置面板
        /// </summary>
        private void NavigateToCardConfigPanel(DeviceBase device)
        {
            if (device == null) return;

            // 根据设备类型调用对应的配置面板导航方法
            // 如果 Model 指示为 PXI-4087C 或名称包含旋变关键词，也导航到 Resolver 面板
            if (device is PxiDeviceBase pxiDevice)
            {
                var modelLower = (pxiDevice.Model ?? string.Empty).ToLowerInvariant();
                if (modelLower.Contains("4087c") || modelLower.Contains("旋变") || modelLower.Contains("resolver"))
                {
                    NavigateToResolverSimulatorConfigPanel(device);
                    return;
                }
            }
            switch (device)
            {
                case AnalogAcquisitionDevice _:
                case AnalogOutputDevice _:
                    NavigateToAnalogInputConfigPanel(device);
                    break;
                case DigitalIODevice _:
                    NavigateToDigitalIOConfigPanel(device);
                    break;
                case CanBusDevice _:
                    NavigateToCanBusConfigPanel(device);
                    break;
                case SwitchDevice _:
                    NavigateToSwitchConfigPanel(device);
                    break;
                case LvdtSimulatorDevice _:
                    NavigateToLvdtSimulatorConfigPanel(device);
                    break;
                case ResolverSimulatorDevice _:
                    NavigateToResolverSimulatorConfigPanel(device);
                    break;
                case Mil1394BDevice _:
                    NavigateToMil1394BConfigPanel(device);
                    break;
                case LvdsDevice _:
                    NavigateToLvdsConfigPanel(device);
                    break;
                case ProgrammableResistorDevice _:
                    NavigateToProgrammableResistorConfigPanel(device);
                    break;
                case Arinc429Device _:
                    NavigateToArinc429ConfigPanel(device);
                    break;
                case Mil1553BDevice _:
                    NavigateToMil1553BConfigPanel(device);
                    break;
                default:
                    // 对于其他板卡类型，暂时不处理
                    break;
            }
        }

        /// <summary>
        /// 导航到数据标定面板（DataCalibration）
        /// </summary>
        public void NavigateToDataCalibration(DeviceBase device, string channelName = null, string channelType = null, string signalName = null, string configTabelName = null)
        {
            // 检查是否已经有标定界面在右侧面板中
            System.Diagnostics.Debug.WriteLine($"[PxiChassis] NavigateToDataCalibration called. RightPanelContent type: {RightPanelContent?.GetType()?.Name ?? "null"}");
            if (RightPanelContent is DataCalibration existingCalibration)
            {
                // 如果已经存在标定界面，则复用并刷新上下文（支持在不同板卡间切换标定）
                System.Diagnostics.Debug.WriteLine("[PxiChassis] Reusing existing DataCalibration panel and refreshing context");

                if (existingCalibration.DataContext is ViewModels.TestTask.ConfigTabel.DataCalibrationViewModel existingViewModel)
                {
                    existingViewModel.SetProjectContext(_projectService?.CurrentProjectRoot);

                    if (device is AnalogAcquisitionDevice existingAnalogAcquisitionDevice)
                    {
                        existingViewModel.ApplyAnalogInputContext(device.Id, existingAnalogAcquisitionDevice.ChannelCount);
                    }
                    else if (device is AnalogOutputDevice existingAnalogOutputDevice)
                    {
                        existingViewModel.ApplyAnalogOutputContext(device.Id, existingAnalogOutputDevice.ChannelCount);
                    }
                }

                return;
            }

            // 创建新的标定界面实例
            System.Diagnostics.Debug.WriteLine("[PxiChassis] Creating new DataCalibration panel");
            var containerProvider = (System.Windows.Application.Current as App)?.Container;
            var viewModel = containerProvider?.Resolve(typeof(ViewModels.TestTask.ConfigTabel.DataCalibrationViewModel)) as ViewModels.TestTask.ConfigTabel.DataCalibrationViewModel;
            if (viewModel == null)
            {
                throw new InvalidOperationException("Failed to resolve DataCalibrationViewModel from dependency injection container. Make sure it's properly registered in App.xaml.cs");
            }

            // 注入当前项目的标定数据（避免错过 ProjectOpened 时发布的 CalibrationRecordsLoadEvent）
            viewModel.SetProjectContext(_projectService?.CurrentProjectRoot);

            if (device is AnalogAcquisitionDevice analogAcquisitionDevice)
            {
                viewModel.ApplyAnalogInputContext(device.Id, analogAcquisitionDevice.ChannelCount);
            }
            else if (device is AnalogOutputDevice analogOutputDevice)
            {
                viewModel.ApplyAnalogOutputContext(device.Id, analogOutputDevice.ChannelCount);
            }

            var dataCalibration = new DataCalibration
            {
                DataContext = viewModel
            };

            System.Diagnostics.Debug.WriteLine($"[PxiChassis] DataCalibration control created. DataContext type: {dataCalibration.DataContext?.GetType()?.Name ?? "null"}");
            RightPanelContent = dataCalibration;
            System.Diagnostics.Debug.WriteLine($"[PxiChassis] DataCalibration panel set to RightPanelContent. RightPanelContent.DataContext type: {(RightPanelContent as System.Windows.FrameworkElement)?.DataContext?.GetType()?.Name ?? "null"}");
        }

        /// <summary>
        /// 处理机箱选择事件
        /// </summary>
        private void OnPxiChassisSelected(PxiChassisSelectedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.ChassisName)) return;

            // 更新机箱名称
            ChassisName = args.ChassisName;

            // 查找对应的机箱设备
            var chassisDevice = FindChassisDeviceByName(args.ChassisName);
            if (chassisDevice != null)
            {
                SelectedDevice = chassisDevice;
            }
            else
            {
            }
        }

        /// <summary>
        /// 处理设备修改事件（用于刷新全局通道编号显示和设备信息更新）
        /// </summary>
        private void OnDeviceModified(DeviceModifiedEventArgs args)
        {
            // 处理通道编号更新事件、设备删除事件和设备更新事件（如重命名）
            if (args.ModificationType == "ChannelNumberingUpdated" ||
                args.ModificationType == "DeviceRemoved" ||
                args.ModificationType == "Update")
            {
                // 如果事件来自当前机箱，需要刷新显示
                if (args.ChassisName == ChassisName)
                {
                    // 触发CollectionChanged事件，刷新TreeView的绑定
                    // 通过替换整个集合引用来强制刷新UI
                    var currentDevices = new ObservableCollection<DeviceBase>(ChassisDevices);
                    ChassisDevices = currentDevices;

                    // 如果当前有选中的设备，也刷新详细信息面板
                    if (SelectedDevice != null)
                    {
                        UpdateDeviceInfoItems(SelectedDevice);

                        // 只有在右侧没有其他内容（或当前就是设备信息面板）时，才刷新设备信息面板，避免覆盖已有配置界面
                        if (RightPanelContent == null || RightPanelContent is DeviceInfoPanel)
                        {
                            NavigateToDeviceInfoPanel();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 根据机箱名称查找机箱设备
        /// </summary>
        private DeviceBase FindChassisDeviceByName(string chassisName)
        {
            return ChassisDevices.FirstOrDefault(device =>
                device.DeviceType == "Chassis" &&
                (device.Name == chassisName || device.Model == chassisName));
        }

        private void OnRenameCard(DeviceBase device)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (device == null || device.DeviceType != "Card")
                return;

            try
            {
                string currentName = device.CardName ?? device.Model;

                // 创建重命名对话框
                var renameDialog = new RenameDialog();
                var viewModel = new RenameDialogViewModel
                {
                    Title = "重命名板卡",
                    OldName = currentName,
                    NewName = currentName
                };

                // 设置验证函数：检查名称唯一性
                viewModel.SetValidateFunc(newName =>
                {
                    // 如果新名称与旧名称相同，允许
                    if (newName == currentName)
                        return true;

                    // 检查名称是否在同一机箱内唯一
                    return _pxiChassisService.ValidateCardName(ChassisName, device.Id, newName);
                });

                renameDialog.DataContext = viewModel;

                // 显示对话框
                if (renameDialog.ShowDialog() == true)
                {
                    string newName = viewModel.NewName?.Trim();

                    // 如果名称没有改变，直接返回
                    if (newName == currentName)
                        return;

                    // 调用服务重命名
                    bool success = _pxiChassisService.RenameCard(ChassisName, device.Id, newName);
                    if (success)
                    {
                        // 发布设备修改事件，通知UI刷新
                        _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
                        {
                            ChassisName = ChassisName,
                            ModificationType = "Update",
                            DeviceInfo = $"板卡重命名: {currentName} -> {newName}"
                        });
                    }
                    else
                    {
                        _dialogService.ShowWarningDialog("重命名失败！该名称可能已被使用。", "");
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowWarningDialog($"重命名失败：{ex.Message}", "错误");
            }
        }

        private void OnDeleteDevice(DeviceBase device)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (device == null) return;

            try
            {
                // 如果要删除的是控制器，检查是否还有其他板卡
                if (device is ControllerDevice)
                {
                    var chassis = _pxiChassisService.GetChassisByName(ChassisName);
                    if (chassis != null && chassis.HasOtherCards())
                    {
                        _dialogService.ShowWarningDialog("请先删除其余板卡！", "");
                        return;
                    }
                }

                // 如果是SwitchDevice，在删除前停止对应的TCP服务器
                if (device is SwitchDevice switchDevice)
                {
                    int port = GetTcpListenPortForSwitchDevice(device);
                    string boardIdentifier = $"PXI2601_{port}";
                    StopTcpServer(boardIdentifier);
                    Debug.WriteLine($"[PxiChassisViewModel] 为SwitchDevice停止TCP服务器: Port={port}, Board={boardIdentifier}");
                }

                // 确认删除
                string confirmMessage;
                if (device.Name == "空槽")
                {
                    var slotText = string.IsNullOrWhiteSpace(device.SlotPosition) ? "" : device.SlotPosition;
                    confirmMessage = $"是否删除槽“{slotText}”？";
                }
                else
                {
                    confirmMessage = $"确定要删除设备 '{device.Model}' 吗？";
                }

                var confirmResult = ReMessageBox.Show(confirmMessage, "确认删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (confirmResult != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
                // 如果是机箱设备，需要特殊处理
                if (device.DeviceType == "Chassis")
                {
                    // 删除机箱设备
                    ChassisDevices.Remove(device);
                    // 从服务中删除机箱设备（确保保存时删除）
                    _pxiChassisService.RemoveDeviceFromChassis(ChassisName, device.Id);
                    // 发布设备修改事件，通知MainWindowViewModel标记项目为已修改并自动保存
                    _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
                    {
                        ChassisName = ChassisName,
                        ModificationType = "Delete",
                        DeviceInfo = $"{device.ParentNode} - {device.Model}"
                    });

                    // 发布项目修改事件，触发自动保存
                    _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                    {
                        ModificationType = "Device",
                        Description = $"删除机箱设备: {device.Model}"
                    });

                    // 注意：不发布DeletePxiChassisEvent事件，因为删除的只是9槽机箱设备，不是整个PXI机箱容器
                    // 整个PXI机箱容器应该保留，只是设备列表为空

                    _dialogService.ShowInfoDialog("机箱设备已删除", "成功");
                }
                else
                {
                    // 删除板卡或其他子设备
                    var chassisDevice = FindChassisDevice();
                    if (chassisDevice?.Children != null && device.DeviceType == "Card")
                    {
                        // 从机箱设备的子节点中删除板卡设备
                        bool removed = chassisDevice.Children.Remove(device);

                        // 更新所有板卡的槽位位置（删除后重新编号）
                        if (chassisDevice is ChassisDevice chassisDeviceForSlot)
                        {
                            UpdateAllSlotPositions(chassisDeviceForSlot);
                        }

                        // 本地机箱：刷新矩阵后台命令处理器注册（槽位可能重排）
                        RefreshLocalMatrixHandlers();

                        // 手动触发Children属性更改通知，确保UI立即更新
                        var children = chassisDevice.Children;
                        chassisDevice.Children = null;
                        chassisDevice.Children = children;
                    }

                    // 从ChassisDevices中删除（如果存在）
                    if (ChassisDevices.Contains(device))
                    {
                        ChassisDevices.Remove(device);
                    }

                    // 从服务中删除设备（确保保存时删除）
                    _pxiChassisService.RemoveDeviceFromChassis(ChassisName, device.Id);
                    // 发布设备修改事件，通知MainWindowViewModel标记项目为已修改
                    _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
                    {
                        ChassisName = ChassisName,
                        ModificationType = "Delete",
                        DeviceInfo = $"{device.ParentNode} - {device.Model}"
                    });

                    // 发布项目修改事件，触发自动保存
                    _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                    {
                        ModificationType = "Device",
                        Description = $"删除设备: {device.ParentNode} - {device.Model}"
                    });

                    _dialogService.ShowInfoDialog("设备已删除", "成功");
                }
                // 更新拖放提示显示状态
                UpdateDropHintVisibility();

                // 清除选中状态
                SelectedDevice = null;

                // 清除右侧设备信息显示
                DeviceInfoItems.Clear();
                DeviceInfoTitle = "暂无信息";
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"删除设备时发生错误: {ex.Message}", "错误");
            }
        }

        private void RefreshLocalMatrixHandlers()
        {
            try
            {
                if (!IsLocalChassisByIp())
                    return;

                var chassisDevice = FindChassisDevice();
                var switches = chassisDevice?.Children?.OfType<SwitchDevice>()
                    .Where(d => d != null && d.SlotIndex > 0)
                    .ToList() ?? new List<SwitchDevice>();

                var newSlots = new HashSet<int>(switches.Select(d => d.SlotIndex));

                // 清理不存在的槽位注册
                var oldSlots = _registeredLocalMatrixHandlerSlots.ToArray();
                foreach (var slot in oldSlots)
                {
                    if (!newSlots.Contains(slot))
                    {
                        while (MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Unregister(slot)) { }
                        _registeredLocalMatrixHandlerSlots.Remove(slot);
                    }
                }

                // 注册新槽位
                foreach (var slot in newSlots)
                {
                    if (_registeredLocalMatrixHandlerSlots.Contains(slot))
                        continue;

                    MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Register(slot, async (args) =>
                    {
                        try
                        {
                            if (args == null) return false;
                            return await ExecuteMatrixCommandInChassisAsync(args.SlotIndex, args.InputNodeId, args.OutputNodeId, args.State).ConfigureAwait(false);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    _registeredLocalMatrixHandlerSlots.Add(slot);
                }
            }
            catch
            {
            }
        }

        private async Task<bool> ExecuteMatrixCommandInChassisAsync(int slotIndex, string inputNodeId, string outputNodeId, byte state)
        {
            try
            {
                var chassisDevice = FindChassisDevice();
                var targetSwitch = chassisDevice?.Children
                    ?.OfType<SwitchDevice>()
                    ?.FirstOrDefault(d => d.SlotIndex == slotIndex);

                if (targetSwitch == null)
                    return false;

                var driverObj = MeasureControl.Drivers.DriverFactory.GetCachedDriver(targetSwitch.Id, slotIndex)
                                ?? MeasureControl.Drivers.DriverFactory.CreateDriver(targetSwitch);

                if (driverObj is MeasureControl.Drivers.ArtSwitch.ArtSwitchDriver artDriver)
                {
                    string topology = null;
                    if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig switchCfg && !string.IsNullOrWhiteSpace(switchCfg.Topology))
                    {
                        topology = switchCfg.Topology;
                    }
                    else
                    {
                        topology = TryMapSlotIndexToTopology(slotIndex);
                    }

                    if (!artDriver.IsConnected)
                    {
                        artDriver.CurrentTopology = topology;
                        var connected = await artDriver.ConnectAsync(topology).ConfigureAwait(false);
                        if (!connected)
                            return false;
                    }

                    bool result;
                    if (state == 0)
                        result = await artDriver.ConnectChannelsWithoutDisconnectAsync(outputNodeId, inputNodeId).ConfigureAwait(false);
                    else
                        result = await artDriver.DisconnectSingleConnectionAsync(outputNodeId, inputNodeId).ConfigureAwait(false);

                    if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig cardConfig)
                    {
                        var newState = state == 0 ? MeasureControl.Models.SwitchConnectionState.Connected : MeasureControl.Models.SwitchConnectionState.Disconnected;
                        cardConfig.SetConnection(inputNodeId, outputNodeId, newState);
                        try { _pxiChassisService?.UpdateDeviceCardConfig(targetSwitch.Id, cardConfig); } catch { }
                    }

                    try
                    {
                        _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Publish(new MeasureControl.Events.DeviceModifiedEventArgs
                        {
                            ChassisName = this.ChassisName,
                            ModificationType = "RemoteCommand",
                            Device = targetSwitch
                        });
                    }
                    catch { }

                    return result;
                }

                if (driverObj is MeasureControl.Drivers.PXI3022.PXI3022Driver pxi3022Driver)
                {
                    if (!pxi3022Driver.IsConnected)
                    {
                        var connected = await pxi3022Driver.ConnectAsync().ConfigureAwait(false);
                        if (!connected)
                            return false;
                    }

                    if (!int.TryParse(inputNodeId?.TrimStart('r', 'R'), out int parsedInputIndex))
                        return false;
                    if (!int.TryParse(outputNodeId?.TrimStart('c', 'C'), out int parsedOutputIndex))
                        return false;

                    int row = parsedInputIndex % 4;
                    int col = parsedOutputIndex % 64;
                    string channelId = $"R{row}C{col}";

                    bool result;
                    if (state == 0)
                        result = await pxi3022Driver.WriteChannelAsync(channelId, 1.0).ConfigureAwait(false);
                    else
                        result = await pxi3022Driver.WriteChannelAsync(channelId, 0.0).ConfigureAwait(false);

                    if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig cardConfig)
                    {
                        var newState = state == 0 ? MeasureControl.Models.SwitchConnectionState.Connected : MeasureControl.Models.SwitchConnectionState.Disconnected;
                        cardConfig.SetConnection(inputNodeId, outputNodeId, newState);
                        try { _pxiChassisService?.UpdateDeviceCardConfig(targetSwitch.Id, cardConfig); } catch { }
                    }

                    try
                    {
                        _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Publish(new MeasureControl.Events.DeviceModifiedEventArgs
                        {
                            ChassisName = this.ChassisName,
                            ModificationType = "RemoteCommand",
                            Device = targetSwitch
                        });
                    }
                    catch { }

                    return result;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsChassisDevice(ProjectItem projectItem)
        {
            // 检查是否包含"机箱"关键字
            if (projectItem.Name.Contains("机箱"))
            {
                return true; // 包含机箱关键字的设备
            }

            // 检查是否为机箱型号（如简仪 PXIe-2722G2, 简仪 PXIe-2519G2等）
            var chassisModels = new[] {
                "PXIe-2722G2", "PXIe-2519G2", "PXIe-27722G2"
            };
            return chassisModels.Any(model => projectItem.Name.Contains(model));
        }

        private bool IsPxiCardDevice(ProjectItem projectItem)
        {
            // 首先检查是否为机箱设备 - 机箱设备应该在最上层
            if (IsChassisDevice(projectItem))
            {
                return false; // 机箱设备不是板卡设备
            }

            if (projectItem?.Name == "空槽" || projectItem?.Name == "盲板")
            {
                return true;
            }

            // 判断是否为PXI板卡设备 - 这些设备应该作为机箱的子节点
            var pxiCardKeywords = new[] {
                "PXIe-", "PXI-", "控制器", "矩阵开关", "离散量", "模拟量", "电阻输出",
                "LVDT", "RVDT", "旋转变压器", "CAN", "ARINC429", "1553B", "1394B", "LVDS",
                "凌华", "欧开", "阿尔泰", "芒果树", "怀智", "简仪", "NI", "National Instruments"
            };
            return pxiCardKeywords.Any(keyword => projectItem.Name.Contains(keyword));
        }

        public void MoveChassisCard(DeviceBase dragged, DeviceBase target, bool insertAfter)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (dragged == null || target == null) return;
            if (dragged is ControllerDevice) return;

            var chassisDevice = FindChassisDevice();
            var chassis = chassisDevice as ChassisDevice;
            if (chassis == null) return;
            if (chassis.Children == null || chassis.Children.Count == 0) return;

            var children = chassis.Children;

            int fromIndex = children.IndexOf(dragged);
            if (fromIndex < 0) return;

            int controllerIndex = -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is ControllerDevice)
                {
                    controllerIndex = i;
                    break;
                }
            }
            int minInsertIndex = controllerIndex >= 0 ? controllerIndex + 1 : 0;

            int targetIndex = children.IndexOf(target);
            if (targetIndex < 0) return;

            if (target is ControllerDevice)
            {
                insertAfter = true;
                targetIndex = controllerIndex;
            }

            int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;
            if (insertIndex > children.Count) insertIndex = children.Count;

            if (fromIndex < insertIndex)
            {
                insertIndex -= 1;
            }

            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;
            if (insertIndex > children.Count - 1) insertIndex = children.Count - 1;

            if (fromIndex == insertIndex) return;

            children.Move(fromIndex, insertIndex);
            UpdateAllSlotPositions(chassis);

            _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
            {
                ChassisName = ChassisName,
                ModificationType = "Reorder",
                DeviceInfo = $"Reorder: {dragged.CardName}"
            });
        }

        public string GetSlotPromptAfterMove(DeviceBase dragged, DeviceBase target, bool insertAfter)
        {
            if (dragged == null || target == null) return null;
            if (dragged is ControllerDevice) return null;

            var chassisDevice = FindChassisDevice();
            var chassis = chassisDevice as ChassisDevice;
            if (chassis?.Children == null || chassis.Children.Count == 0) return null;

            var children = chassis.Children;
            int fromIndex = children.IndexOf(dragged);
            if (fromIndex < 0) return null;

            int controllerIndex = -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is ControllerDevice)
                {
                    controllerIndex = i;
                    break;
                }
            }
            int minInsertIndex = controllerIndex >= 0 ? controllerIndex + 1 : 0;

            int targetIndex = children.IndexOf(target);
            if (targetIndex < 0) return null;

            if (target is ControllerDevice)
            {
                insertAfter = true;
                targetIndex = controllerIndex;
            }

            int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;
            if (insertIndex > children.Count) insertIndex = children.Count;

            if (fromIndex < insertIndex)
            {
                insertIndex -= 1;
            }

            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;
            if (insertIndex > children.Count - 1) insertIndex = children.Count - 1;
            if (fromIndex == insertIndex) return null;

            var simulated = children.ToList();
            simulated.Remove(dragged);
            if (insertIndex > simulated.Count) insertIndex = simulated.Count;
            simulated.Insert(insertIndex, dragged);

            var slotIndex = GetSlotIndexFromSequence(simulated, dragged);
            return slotIndex.HasValue ? $"Slot{slotIndex.Value}" : null;
        }

        public void MoveChassisCardToEnd(DeviceBase dragged)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (dragged == null) return;
            if (dragged is ControllerDevice) return;

            var chassisDevice = FindChassisDevice();
            var chassis = chassisDevice as ChassisDevice;
            if (chassis == null) return;
            if (chassis.Children == null || chassis.Children.Count == 0) return;

            var children = chassis.Children;
            int fromIndex = children.IndexOf(dragged);
            if (fromIndex < 0) return;

            int controllerIndex = -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is ControllerDevice)
                {
                    controllerIndex = i;
                    break;
                }
            }
            int minInsertIndex = controllerIndex >= 0 ? controllerIndex + 1 : 0;

            int insertIndex = children.Count - 1;
            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;

            if (fromIndex == insertIndex) return;
            children.Move(fromIndex, insertIndex);
            UpdateAllSlotPositions(chassis);

            _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
            {
                ChassisName = ChassisName,
                ModificationType = "Reorder",
                DeviceInfo = $"Reorder: {dragged.CardName}"
            });
        }

        public string GetSlotPromptAfterMoveToEnd(DeviceBase dragged)
        {
            if (dragged == null) return null;
            if (dragged is ControllerDevice) return null;

            var chassisDevice = FindChassisDevice();
            var chassis = chassisDevice as ChassisDevice;
            if (chassis?.Children == null || chassis.Children.Count == 0) return null;

            var children = chassis.Children;
            int fromIndex = children.IndexOf(dragged);
            if (fromIndex < 0) return null;

            int controllerIndex = -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is ControllerDevice)
                {
                    controllerIndex = i;
                    break;
                }
            }
            int minInsertIndex = controllerIndex >= 0 ? controllerIndex + 1 : 0;

            int insertIndex = children.Count - 1;
            if (insertIndex < minInsertIndex) insertIndex = minInsertIndex;
            if (fromIndex == insertIndex) return null;

            var simulated = children.ToList();
            simulated.Remove(dragged);
            simulated.Add(dragged);

            var slotIndex = GetSlotIndexFromSequence(simulated, dragged);
            return slotIndex.HasValue ? $"Slot{slotIndex.Value}" : null;
        }

        private int? GetSlotIndexFromSequence(IReadOnlyList<DeviceBase> sequence, DeviceBase targetDevice)
        {
            if (sequence == null || targetDevice == null) return null;

            int currentSlot = 1;
            foreach (var child in sequence)
            {
                if (child is ControllerDevice ctrl)
                {
                    currentSlot += Math.Max(1, ctrl.SlotsOccupied);
                    continue;
                }

                if (child?.DeviceType == "Card")
                {
                    if (ReferenceEquals(child, targetDevice))
                    {
                        return currentSlot;
                    }
                    currentSlot += 1;
                }
            }

            return null;
        }

        private DeviceBase FindChassisDevice()
        {
            return ChassisDevices.FirstOrDefault(d => d.DeviceType == "Chassis");
        }

        private string GetParentNodeName(ProjectItem projectItem)
        {
            // 首先尝试从项目树结构中获取父节点名称
            var parentNodeName = GetParentNodeFromProjectTree(projectItem);
            if (!string.IsNullOrEmpty(parentNodeName))
            {
                return parentNodeName;
            }

            // 如果无法从项目树获取，则根据设备名称推断类型节点
            if (projectItem.Name.Contains("机箱"))
            {
                // 使用 ChassisFactory 动态获取槽位数
                int slotCount = MeasureControl.Helpers.ChassisFactory.GetSlotCount(projectItem.Name);
                return $"{slotCount}槽机箱";
            }
            if (projectItem.Name.Contains("控制器")) return "控制器";
            if (projectItem.Name.Contains("矩阵开关")) return "矩阵开关";
            if (projectItem.Name.Contains("离散量")) return "离散量";
            if (projectItem.Name.Contains("模拟量")) return "模拟量";
            if (projectItem.Name.Contains("电阻输出")) return "电阻输出";
            if (projectItem.Name.Contains("LVDT") || projectItem.Name.Contains("RVDT")) return "LVDT/RVDT";
            if (projectItem.Name.Contains("旋转变压器")) return "旋转变压器";
            if (projectItem.Name.Contains("CAN")) return "CAN";
            if (projectItem.Name.Contains("ARINC429")) return "ARINC429";
            if (projectItem.Name.Contains("1553B")) return "1553B";
            if (projectItem.Name.Contains("1394B")) return "1394B";
            if (projectItem.Name.Contains("LVDS")) return "LVDS";

            // PXI板卡设备类型识别（简仪、NI等品牌）
            if (projectItem.Name.Contains("简仪") || projectItem.Name.Contains("NI") || projectItem.Name.Contains("National Instruments"))
            {
                // 根据具体型号确定类型
                if (projectItem.Name.Contains("PXIe-") || projectItem.Name.Contains("PXI-"))
                {
                    // 如果是PXI板卡但没有具体类型，默认为PXI板卡
                    return "PXI板卡";
                }
            }

            // 程控设备类型识别
            if (projectItem.Name.Contains("电源")) return "程控电源";
            if (projectItem.Name.Contains("负载")) return "电子负载";

            // Chroma设备具体类型识别
            if (projectItem.Name.Contains("Chroma 6314A")) return "程控电源";
            if (projectItem.Name.Contains("Chroma 6312A")) return "电子负载";
            if (projectItem.Name.Contains("Chroma")) return "程控仪器仪表";

            // 程控仪器仪表子类型识别
            if (projectItem.Name.Contains("DG1032Z") || projectItem.Name.Contains("信号发生器")) return "信号发生器";
            if (projectItem.Name.Contains("DM3068") || projectItem.Name.Contains("数字多用表")) return "数字多用表";
            if (projectItem.Name.Contains("DH04804") || projectItem.Name.Contains("示波器")) return "示波器";
            if (projectItem.Name.Contains("53220A") || projectItem.Name.Contains("频率计")) return "频率计";
            if (projectItem.Name.Contains("6314A")) return "串口";

            // 其他程控仪器仪表设备
            if (projectItem.Name.Contains("普源") || projectItem.Name.Contains("是德") ||
                projectItem.Name.Contains("DH") || projectItem.Name.Contains("MS")) return "程控仪器仪表";

            return "其他自定义设备";
        }

        /// <summary>
        /// 解析设备名称，提取制造商和型号
        /// </summary>
        private void ParseDeviceName(DeviceBase device, string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                device.Name = "N/A";
                device.Manufacturer = "N/A";
                device.Model = "N/A";
                return;
            }

            // 解析设备名称，格式：制造商 型号
            var parts = deviceName.Split(' ');
            if (parts.Length >= 2)
            {
                device.Manufacturer = parts[0];
                device.Model = string.Join(" ", parts.Skip(1));
                device.Name = deviceName;
            }
            else
            {
                device.Name = deviceName;
                device.Manufacturer = "N/A";
                device.Model = "N/A";
            }
        }

        /// <summary>
        /// 从项目树结构中获取设备的父节点名称
        /// </summary>
        private string GetParentNodeFromProjectTree(ProjectItem projectItem)
        {
            // 遍历所有工具项，查找包含该设备的父节点
            foreach (var tool in Tools)
            {
                var parentNode = FindParentNodeInChildren(tool, projectItem);
                if (!string.IsNullOrEmpty(parentNode))
                {
                    return parentNode;
                }
            }
            return null;
        }

        /// <summary>
        /// 在子节点中递归查找父节点
        /// </summary>
        private string FindParentNodeInChildren(ProjectItem parent, ProjectItem target)
        {
            if (parent.Children == null) return null;

            foreach (var child in parent.Children)
            {
                // 如果找到目标设备，返回父节点名称
                if (child.Name == target.Name)
                {
                    return parent.Name;
                }

                // 递归查找子节点
                var result = FindParentNodeInChildren(child, target);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }

            return null;
        }

        private string GetDeviceDescription(ProjectItem projectItem)
        {
            if (projectItem.Name.Contains("电源")) return "程控电源";
            if (projectItem.Name.Contains("负载")) return "电子负载";
            if (projectItem.Name.Contains("普源") || projectItem.Name.Contains("是德") ||
                projectItem.Name.Contains("Chroma") || projectItem.Name.Contains("DH") ||
                projectItem.Name.Contains("MS")) return "程控仪器仪表";
            if (projectItem.Name.Contains("控制器")) return "PXI控制器";
            if (projectItem.Name.Contains("矩阵开关")) return "矩阵开关";
            if (projectItem.Name.Contains("离散量")) return "离散量输入输出";
            if (projectItem.Name.Contains("模拟量")) return "模拟量采集";
            if (projectItem.Name.Contains("电阻输出")) return "电阻输出";
            if (projectItem.Name.Contains("LVDT") || projectItem.Name.Contains("RVDT")) return "LVDT/RVDT模拟";
            if (projectItem.Name.Contains("旋转变压器")) return "旋转变压器模拟";
            if (projectItem.Name.Contains("CAN")) return "CAN通信";
            if (projectItem.Name.Contains("ARINC429")) return "ARINC429通信";
            if (projectItem.Name.Contains("1553B")) return "1553B通信";
            if (projectItem.Name.Contains("1394B")) return "1394B通信";
            if (projectItem.Name.Contains("LVDS")) return "LVDS通信";

            return "PXI板卡";
        }

        private string GetSamplingRate(ProjectItem projectItem)
        {
            if (projectItem.Name.Contains("电源") || projectItem.Name.Contains("负载")) return "N/A";
            return "1 MS/s";
        }

        private string GetChannelCount(ProjectItem projectItem)
        {
            if (projectItem.Name.Contains("电源") || projectItem.Name.Contains("负载")) return "1";
            if (projectItem.Name.Contains("普源") || projectItem.Name.Contains("是德") ||
                projectItem.Name.Contains("Chroma") || projectItem.Name.Contains("DH") ||
                projectItem.Name.Contains("MS")) return "2";
            return "16";
        }


        private void UpdateDropHintVisibility()
        {
            // 当没有设备时显示拖放提示
            ShowDropHint = ChassisDevices.Count == 0;
        }

        /// <summary>
        /// 展开PXI工具树的一级目录
        /// </summary>
        private void ExpandPxiToolsTreeLevel2()
        {
            // 使用Dispatcher延迟执行，确保UI完全加载后再展开
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 查找PxiChassis页面并调用展开方法
                    var pxiChassisView = Application.Current.Windows.OfType<Window>()
                        .SelectMany(w => FindVisualChildren<PxiChassis>(w))
                        .FirstOrDefault();
                    pxiChassisView?.ExpandPxiToolsTreeLevel2();
                }
                catch (Exception)
                {
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 在可视化树中查找指定类型的子元素
        /// </summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        #region INavigationAware Implementation
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 缓存导航日志用于关闭时回退
            _journal = navigationContext?.NavigationService?.Journal;
            // 获取传入的机箱名称
            if (navigationContext.Parameters.ContainsKey("ChassisName"))
            {
                // 首先清除旧的选中状态和右侧信息面板
                SelectedDevice = null;
                DeviceInfoItems?.Clear();
                DeviceInfoTitle = "暂无信息";

                ChassisName = navigationContext.Parameters["ChassisName"].ToString();
                // 确保机箱存在
                InitializeChassis();
                // 加载对应机箱的设备数据
                LoadChassisDevices();

                EnsureFixedDemoLayoutIfNeeded();

                // 检查是否为标定导航
                bool isCalibrationNavigation = navigationContext.Parameters.ContainsKey("IsCalibrationNavigation") &&
                    navigationContext.Parameters["IsCalibrationNavigation"] is bool isCalibration && isCalibration;

                if (isCalibrationNavigation)
                {
                    // 处理标定导航：选中对应板卡并导航到DataCalibration
                    string cardName = navigationContext.Parameters["CardName"]?.ToString();
                    string channelName = navigationContext.Parameters["ChannelName"]?.ToString();
                    string channelType = navigationContext.Parameters["ChannelType"]?.ToString();
                    string signalName = navigationContext.Parameters["SignalName"]?.ToString();
                    string configTabelName = navigationContext.Parameters["ConfigTabelName"]?.ToString();

                    if (!string.IsNullOrEmpty(cardName))
                    {
                        // 查找并选中对应的板卡设备
                        var chassisDevice = FindChassisDevice();
                        if (chassisDevice?.Children != null)
                        {
                            var cardDevice = chassisDevice.Children.FirstOrDefault(d =>
                                d.DeviceType == "Card" &&
                                (!string.IsNullOrEmpty(d.CardName) ? d.CardName == cardName : d.Model == cardName));

                            if (cardDevice != null)
                            {
                                // 注意：不调用 OnDeviceClick，避免重新创建右侧面板导致板卡状态丢失
                                // 只设置 SelectedDevice，保持右侧面板的缓存状态
                                SelectedDevice = cardDevice;

                                // 导航到DataCalibration界面，传递信号和通道信息
                                NavigateToDataCalibration(cardDevice, channelName, channelType, signalName, configTabelName);
                            }
                        }
                    }
                }

                // 检查是否为波形显示导航
                bool isWaveformNavigation = navigationContext.Parameters.ContainsKey("IsWaveformNavigation") &&
                    navigationContext.Parameters["IsWaveformNavigation"] is bool isWaveform && isWaveform;

                if (isWaveformNavigation)
                {
                    // 处理波形显示导航：选中对应板卡并打开通道配置面板
                    string cardName = navigationContext.Parameters["CardName"]?.ToString();

                    if (!string.IsNullOrEmpty(cardName))
                    {
                        // 查找并选中对应的板卡设备
                        var chassisDevice = FindChassisDevice();
                        if (chassisDevice?.Children != null)
                        {
                            var cardDevice = chassisDevice.Children.FirstOrDefault(d =>
                                d.DeviceType == "Card" &&
                                (!string.IsNullOrEmpty(d.CardName) ? d.CardName == cardName : d.Model == cardName));

                            if (cardDevice != null)
                            {
                                // 注意：不调用 OnDeviceClick，避免重新创建右侧面板导致板卡状态丢失
                                // 只设置 SelectedDevice，保持右侧面板的缓存状态
                                SelectedDevice = cardDevice;

                                // 根据板卡类型导航到对应的配置面板
                                if (cardDevice is AnalogAcquisitionDevice || cardDevice is AnalogOutputDevice)
                                {
                                    NavigateToAnalogInputConfigPanel(cardDevice);
                                }
                                else if (cardDevice is DigitalIODevice)
                                {
                                    NavigateToDigitalIOConfigPanel(cardDevice);
                                }
                            }
                        }
                    }
                }

                // 展开PXI工具树的一级目录
                ExpandPxiToolsTreeLevel2();
            }
        }

        private void EnsureFixedDemoLayoutIfNeeded()
        {
            if (!FixedDemoMode)
            {
                return;
            }

            if (!string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal) &&
                !string.Equals(ChassisName, "PXI机箱2", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var expectedNames = new List<string>();
                var sequence = new List<string>();
                var chassisModel = "";

                if (string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal))
                {
                    chassisModel = "PXIe-2722G2";
                    expectedNames = new List<string>
                    {
                        "凌华 PXIe-3987",
                        "欧开 PXI-4087A",
                         "欧开 PXI-4087C",
                        "欧开 PXI-4087C",
                        "阿尔泰 PXI-7012",
                        "阿尔泰 PXI-7012",
                       "芒果树 MT-X532",
                         "阿尔泰 PXIe-4227",
                        "阿尔泰 PXIe-9774",
                        "空槽",
                       "阿尔泰 PXI-4004",
                        "简仪 PXIe-7131",
                        "芒果树 MT-X970",
                        "阿尔泰 PXI-4332",
                        "怀智 HZ-MIL1394B-PX1e-4N",
                        "空槽",
                        "空槽",
                        "空槽"
                    };

                    // 与 expectedNames 保持一致，避免每次进入都因 "盲板" vs "空槽" 不一致而重建
                    sequence = new List<string>(expectedNames);
                }
                else
                {
                    // PXI机箱2：先用空槽占位填满（9槽），留接口后续替换字符串即可
                    chassisModel = "PXIe-2519G2";
                    expectedNames = expectedNames = new List<string>
                    {
                        "凌华 PXIe-3987",
                        "欧开 PXI-3022",
                        "欧开 PXI-3022",
                        "阿尔泰 PXI-2601",
                        "空槽",
                        "阿尔泰 PXI-2601",
                        "阿尔泰 PXI-2601",
                        "阿尔泰 PXI-2601",
                        "阿尔泰 PXI-2601",                       
                    };

                    sequence = new List<string>(expectedNames);
                }

                var chassisDevice = _pxiChassisService.EnsureChassisDevice(ChassisName, chassisModel);
                if (chassisDevice == null)
                {
                    return;
                }

                LoadChassisDevices();

                var uiChassisDevice = FindChassisDevice() as ChassisDevice;
                if (uiChassisDevice?.Children == null)
                {
                    return;
                }

                // 仅在“新建项目/机箱无数据”时初始化一次默认布局。
                // 一旦 proj.json 中已有任何板卡/仪器数据，则不再校验、不再重建，避免覆盖配置项。
                if (uiChassisDevice.Children.Count > 0)
                {
                    return;
                }

                if (string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal))
                {
                    var hasAnyInstrument = false;
                    try
                    {
                        hasAnyInstrument = ChassisDevices.Any(d => d != null && string.Equals(d.DeviceType, "Instrument", StringComparison.Ordinal));
                    }
                    catch
                    {
                        hasAnyInstrument = false;
                    }

                    if (hasAnyInstrument)
                    {
                        return;
                    }
                }

                _isApplyingFixedDemoLayout = true;
                try
                {
                    foreach (var name in sequence)
                    {
                        var toolItem = FindToolItemByName(name);
                        if (toolItem == null)
                        {
                            toolItem = new ProjectItem { Name = name };
                        }

                        OnAddDevice(toolItem);
                    }

                    EnsureRequiredFixedDemoInstruments();
                    ApplyFixedDemoInstrumentDefaults();
                    LoadChassisDevices();
                }
                finally
                {
                    _isApplyingFixedDemoLayout = false;
                }
            }
            catch
            {
            }
        }

        private bool HasRequiredFixedDemoInstruments()
        {
            if (!string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal))
            {
                return true;
            }

            var required = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "普源 DG1032Z", 1 },
                { "普源 DM3068", 1 },
                { "是德 53220A", 1 },
                { "普源 DH04804", 1 },
                { "艾德克斯 IT-N6332B", 3 },
                { "RS422模块", 2 },
                { "RS232模块", 1 },
            };

            var current = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in ChassisDevices)
            {
                if (d == null) continue;
                if (!string.Equals(d.DeviceType, "Instrument", StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(d.Name)) continue;

                if (!current.ContainsKey(d.Name)) current[d.Name] = 0;
                current[d.Name] += 1;
            }

            foreach (var kv in required)
            {
                current.TryGetValue(kv.Key, out var count);
                if (count < kv.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureRequiredFixedDemoInstruments()
        {
            if (!string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal))
            {
                return;
            }

            var required = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "普源 DG1032Z", 1 },
                { "普源 DM3068", 1 },
                { "是德 53220A", 1 },
                { "普源 DH04804", 1 },
                { "艾德克斯 IT-N6332B", 3 },
                { "RS422模块", 2 },
                { "RS232模块", 1 },
            };

            var current = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in ChassisDevices)
            {
                if (d == null) continue;
                if (!string.Equals(d.DeviceType, "Instrument", StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(d.Name)) continue;

                if (!current.ContainsKey(d.Name)) current[d.Name] = 0;
                current[d.Name] += 1;
            }

            foreach (var kv in required)
            {
                current.TryGetValue(kv.Key, out var count);
                int need = kv.Value - count;
                for (int i = 0; i < need; i++)
                {
                    var toolItem = FindToolItemByName(kv.Key) ?? new ProjectItem { Name = kv.Key };
                    OnAddDevice(toolItem);
                }
            }

            ApplyFixedDemoInstrumentDefaults();
        }

        private void ApplyFixedDemoInstrumentDefaults()
        {
            if (!FixedDemoMode) return;
            if (!string.Equals(ChassisName, "PXI机箱1", StringComparison.Ordinal)) return;

            try
            {
                // 1) 去掉同级设备显示名尾部编号（1/2/3...）
                foreach (var d in ChassisDevices)
                {
                    if (d == null) continue;
                    if (!string.Equals(d.DeviceType, "Instrument", StringComparison.Ordinal)) continue;

                    var dn = d.DisplayName;
                    if (string.IsNullOrWhiteSpace(dn)) continue;

                    var trimmed = StripTrailingDigits(dn);
                    if (!string.Equals(trimmed, dn, StringComparison.Ordinal))
                    {
                        d.DisplayName = trimmed;
                    }
                }

                // 2) 三台电源默认IP（按当前列表顺序）
                var psList = new List<PowerSupplyDevice>();
                for (int i = 0; i < ChassisDevices.Count; i++)
                {
                    var ps = ChassisDevices[i] as PowerSupplyDevice;
                    if (ps == null) continue;
                    if (ps.Model != null && ps.Model.IndexOf("IT-N6332", StringComparison.OrdinalIgnoreCase) < 0 &&
                        ps.Name != null && ps.Name.IndexOf("IT-N6332", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    psList.Add(ps);
                }

                var ips = new[] { "192.168.1.15", "192.168.1.16", "192.168.1.17" };
                for (int i = 0; i < ips.Length && i < psList.Count; i++)
                {
                    psList[i].IpAddress = ips[i];
                }
            }
            catch
            {
            }
        }

        private static string StripTrailingDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int end = text.Length;
            while (end > 0 && char.IsDigit(text[end - 1]))
            {
                end--;
            }
            return end == text.Length ? text : text.Substring(0, end).TrimEnd();
        }

        private ProjectItem FindToolItemByName(string name)
        {
            if (Tools == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var root in Tools)
            {
                var found = FindToolItemByNameRecursive(root, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private ProjectItem FindToolItemByNameRecursive(ProjectItem current, string name)
        {
            if (current == null)
            {
                return null;
            }

            if (string.Equals(current.Name, name, StringComparison.Ordinal))
            {
                return current;
            }

            if (current.Children == null)
            {
                return null;
            }

            foreach (var child in current.Children)
            {
                var found = FindToolItemByNameRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 如果导航到同一个机箱，复用现有实例，保持板卡运行状态和前端显示状态
            // 只有当机箱名称不同时才创建新实例
            if (navigationContext.Parameters.ContainsKey("ChassisName"))
            {
                string targetChassisName = navigationContext.Parameters["ChassisName"]?.ToString();
                return !string.IsNullOrEmpty(targetChassisName) && targetChassisName == this.ChassisName;
            }
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 注意：不清理状态，保持板卡运行状态和前端显示
            // 只有在真正关闭 PxiChassis 时（点击关闭按钮）才会清理
            // 这样即使离开去标定页面或其他页面，重新进入时也能保持状态
            // SelectedDevice 和 RightPanelContent 保持不变
        }

        /// <summary>
        /// 处理项目关闭事件
        /// </summary>
        private void OnProjectClosed()
        {
            ResourceCleanupHelper.TryCleanup(() =>
            {
                // 清理机箱设备数据，包括子设备
                ResourceCleanupHelper.CleanupDeviceCollection(ChassisDevices);

                // 清理设备信息
                ResourceCleanupHelper.CleanupCollection(DeviceInfoItems);

                // 清理选中的设备
                SelectedDevice = null;

                // 重置机箱名称
                ChassisName = $"{AppConstants.DefaultChassisNamePrefix}1";

                // 重新初始化可用机箱列表
                InitializeAvailableChassis();

            }, "PxiChassisViewModel项目关闭清理");
        }
        #endregion

        #region TCP Server Management Methods

        /// <summary>
        /// 获取本地IPv4地址列表
        /// </summary>
        private static string[] GetLocalIpv4Addresses()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Where(a => !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 判断是否为本地机箱（通过IP地址判断）
        /// </summary>
        private bool IsLocalChassisByIp()
        {
            var ips = GetLocalIpv4Addresses();
            if (ips.Contains(LocalChassisIpAddress)) return true;
            if (ips.Contains(RemoteClientIpAddress)) return false;
            return false;
        }

        /// <summary>
        /// 获取Switch设备的TCP监听端口
        /// </summary>
        private int GetTcpListenPortForSwitchDevice(DeviceBase device)
        {
            // 根据设备ID或槽位位置计算端口号
            int slotIndex = 1; // 默认槽位

            // 优先使用 device.SlotIndex（如果已经被正确设置）
            if (device is PxiDeviceBase pxiDevice && pxiDevice.SlotIndex > 0)
            {
                slotIndex = pxiDevice.SlotIndex;
            }
            else if (!string.IsNullOrWhiteSpace(device.SlotPosition) && device.SlotPosition.StartsWith("Slot"))
            {
                // 尝试从设备的SlotPosition解析槽位索引（例如 "Slot 4" 或 "Slot4"）
                if (int.TryParse(device.SlotPosition.Replace("Slot", "").Trim(), out int parsedSlot))
                {
                    slotIndex = parsedSlot;
                }
            }
            else
            {
                // 如果没有明确的 SlotPosition 或 SlotIndex，根据设备在机箱中的位置确定
                var chassisDevice = FindChassisDevice();
                if (chassisDevice?.Children != null)
                {
                    var index = chassisDevice.Children.IndexOf(device);
                    if (index >= 0)
                    {
                        slotIndex = index + 1; // 1-based indexing
                    }
                    else
                    {
                        // 诊断日志：如果找不到设备，输出子设备列表，便于定位问题
                        Debug.WriteLine($"[PxiChassisViewModel] GetTcpListenPortForSwitchDevice: Device not found in chassis.Children. Device.Id={device?.Id}, Device.Name={device?.Name}");
                        for (int i = 0; i < chassisDevice.Children.Count; i++)
                        {
                            var child = chassisDevice.Children[i];
                            Debug.WriteLine($"[PxiChassisViewModel] child[{i}] Id={child?.Id}, Name={child?.Name}, SlotPosition='{child?.SlotPosition}', SlotIndex={(child as PxiDeviceBase)?.SlotIndex}");
                        }
                    }
                }
            }

            // 根据设备类型选择不同的端口范围
            int basePort = TcpBasePort2601; // 默认使用2601系列端口

            // 检查是否为PXI-3022设备，使用不同的端口范围
            if (device != null && !string.IsNullOrWhiteSpace(device.Model))
            {
                if (device.Model.Contains("3022") || device.Model.Contains("PXI3022") || device.Model.Contains("PXI-3022"))
                {
                    basePort = TcpBasePort3022; // PXI-3022使用50300系列端口
                }
            }

            int port = basePort + slotIndex;
            Debug.WriteLine($"[PxiChassisViewModel] GetTcpListenPortForSwitchDevice: Device={device?.Name}, Model={device?.Model}, SlotIndex={slotIndex}, BasePort={basePort}, Port={port}");
            return port;
        }

        /// <summary>
        /// 启动指定端口的TCP服务器
        /// </summary>
        private void StartTcpServerForPort(int port, string boardIdentifier)
        {
            try
            {
                // 使用共享管理器启动/复用 TCP Server，并把客户端处理委托交给当前 ViewModel 的 HandleClientAsync
                bool ok = TcpServerManager.Instance.Start(port, boardIdentifier, async (client, serverInfo, token) =>
                {
                    await HandleClientAsync(client, serverInfo, token);
                });

                if (ok)
                {
                    _ownedTcpServerIdentifiers.Add(boardIdentifier);
                    Debug.WriteLine($"[StartTcpServerForPort] (via manager) 启动或复用 TCP 服务器: 端口={port}, 板卡={boardIdentifier}");
                }
                else
                {
                    Debug.WriteLine($"[StartTcpServerForPort] (via manager) 启动失败: 端口={port}, 板卡={boardIdentifier}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartTcpServerForPort] 启动失败: {ex.Message}");
                try { TcpServerManager.Instance.Stop(boardIdentifier); } catch { }
            }
        }

        /// <summary>
        /// 停止指定板卡的TCP服务器
        /// </summary>
        private void StopTcpServer(string boardIdentifier)
        {
            try
            {
                TcpServerManager.Instance.Stop(boardIdentifier);
                _ownedTcpServerIdentifiers.Remove(boardIdentifier);
                Debug.WriteLine($"[StopTcpServer] (via manager) 停止/减少引用: {boardIdentifier}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StopTcpServer] 停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止所有拥有的TCP服务器
        /// </summary>
        private void StopAllTcpServers()
        {
            var identifiers = _ownedTcpServerIdentifiers.ToArray();
            foreach (var identifier in identifiers)
            {
                StopTcpServer(identifier);
            }
        }

        /// <summary>
        /// TCP服务器接受循环
        /// </summary>
        private async Task AcceptLoopAsync(TcpServerInfo serverInfo, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        var acceptTask = serverInfo.Listener.AcceptTcpClientAsync();
                        var completed = await Task.WhenAny(acceptTask, Task.Delay(Timeout.Infinite, token));
                        if (completed != acceptTask)
                            break;

                        client = acceptTask.Result;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AcceptLoopAsync] 异常: {ex.Message}");
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(client, serverInfo, token));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcceptLoopAsync] 循环异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理TCP客户端连接
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, TcpServerInfo serverInfo, CancellationToken token)
        {
            try
            {
                if (client == null) return;
                Debug.WriteLine($"[HandleClientAsync] Accepted Remote={client.Client?.RemoteEndPoint} Local={client.Client?.LocalEndPoint} Board={serverInfo?.BoardIdentifier} Port={serverInfo?.Port}");
                using (var stream = client.GetStream())
                {
                    var cmd = new byte[3];

                    while (!token.IsCancellationRequested)
                    {
                        int read = await ReadExactAsync(stream, cmd, 0, cmd.Length, token);
                        if (read != cmd.Length) continue;

                        byte inputIndex = cmd[0];
                        byte outputIndex = cmd[1];
                        byte state = cmd[2];

                        Debug.WriteLine($"[HandleClientAsync] RX({serverInfo?.Port}): {BitConverter.ToString(cmd)} => r{inputIndex},c{outputIndex},state={state}");

                        // 处理来自远程客户端的矩阵控制命令
                        try
                        {
                            // 根据端口范围确定正确的基端口，然后计算槽位索引
                            int basePort;
                            int port = serverInfo?.Port ?? TcpBasePort2601;

                            // 根据端口范围判断设备类型
                            if (port >= TcpBasePort3022 && port < TcpBasePort3022 + 100)
                            {
                                basePort = TcpBasePort3022; // PXI-3022使用50300系列端口
                            }
                            else
                            {
                                basePort = TcpBasePort2601; // PXI-2601使用50200系列端口
                            }

                            int slotIndex = port - basePort;

                            // 添加调试日志
                            Debug.WriteLine($"[HandleClientAsync] Port calculation: Port={port}, BasePort={basePort}, CalculatedSlotIndex={slotIndex}");
                            string inputNodeId = $"r{inputIndex}";
                            string outputNodeId = $"c{outputIndex}";

                            Debug.WriteLine($"[HandleClientAsync] Parsed command -> SlotIndex={slotIndex}, {inputNodeId} -> {outputNodeId}, state={state}");

                            // 在 chassis.Children 中查找对应槽位的 SwitchDevice（优先使用 SlotIndex）
                            var chassisDevice = FindChassisDevice();
                            SwitchDevice targetSwitch = null;
                            if (chassisDevice?.Children != null)
                            {
                                targetSwitch = chassisDevice.Children
                                    .OfType<SwitchDevice>()
                                    .FirstOrDefault(d => d.SlotIndex == slotIndex);
                                if (targetSwitch == null)
                                {
                                    // 退回到根据 SlotPosition 匹配
                                    targetSwitch = chassisDevice.Children
                                        .OfType<SwitchDevice>()
                                        .FirstOrDefault(d => string.Equals(d.SlotPosition, $"Slot {slotIndex}", StringComparison.OrdinalIgnoreCase));
                                }
                            }

                            if (targetSwitch != null)
                            {
                                // 将接收到的远程矩阵命令转发给对应的 PXI2601_SWITCHViewModel 处理（UI 层）
                                var evtArgs = new MeasureControl.Events.RemoteMatrixCommandEventArgs
                                {
                                    SlotIndex = slotIndex,
                                    InputNodeId = inputNodeId,
                                    OutputNodeId = outputNodeId,
                                    State = state,
                                    Port = serverInfo?.Port ?? 0,
                                    BoardIdentifier = serverInfo?.BoardIdentifier
                                };

                                Debug.WriteLine($"[HandleClientAsync] Dispatching RemoteMatrixCommandEvent: SlotIndex={slotIndex}, {inputNodeId}->{outputNodeId}, state={state}");
                                bool dispatched = false;
                                try
                                {
                                    // 优先通过 dispatcher 直接路由到已注册的 PXI2601_SWITCHViewModel（若存在）
                                    dispatched = await MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.DispatchAsync(evtArgs).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[HandleClientAsync] Dispatcher exception: {ex.Message}");
                                }

                                // 兼容性：继续发布事件（供其他订阅方使用）
                                try
                                {
                                    _eventAggregator?.GetEvent<MeasureControl.Events.RemoteMatrixCommandEvent>()?.Publish(evtArgs);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[HandleClientAsync] 发布 RemoteMatrixCommandEvent 异常: {ex.Message}");
                                }

                                // 如果没有任何 ViewModel 处理（例如面板未打开），回退到机箱端直接执行硬件操作
                                if (dispatched)
                                //if (!dispatched)
                                {
                                    Debug.WriteLine($"[HandleClientAsync] No viewmodel handled command for slot {slotIndex}, performing fallback execution in chassis.");
                                    try
                                    {
                                        // 获取或创建驱动（DriverFactory 会缓存）
                                        var driverObj = MeasureControl.Drivers.DriverFactory.GetCachedDriver(targetSwitch.Id, slotIndex)
                                                        ?? MeasureControl.Drivers.DriverFactory.CreateDriver(targetSwitch);

                                        // 处理不同的开关驱动类型
                                        if (driverObj is MeasureControl.Drivers.ArtSwitch.ArtSwitchDriver artDriver)
                                        {
                                            // PXI-2601等使用ArtSwitch驱动的设备
                                            string topology = null;
                                            if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig switchCfg && !string.IsNullOrWhiteSpace(switchCfg.Topology))
                                            {
                                                topology = switchCfg.Topology;
                                            }
                                            else
                                            {
                                                // 根据槽位自动映射拓扑
                                                topology = TryMapSlotIndexToTopology(slotIndex);
                                            }

                                            if (!artDriver.IsConnected)
                                            {
                                                artDriver.CurrentTopology = topology;
                                                var connected = await artDriver.ConnectAsync(topology).ConfigureAwait(false);
                                                Debug.WriteLine($"[HandleClientAsync-Fallback] artDriver.ConnectAsync result: {connected}");
                                            }

                                            if (artDriver.IsConnected)
                                            {
                                                bool result = false;
                                                if (state == 0)
                                                    result = await artDriver.ConnectChannelsWithoutDisconnectAsync(outputNodeId, inputNodeId).ConfigureAwait(false);
                                                else
                                                    result = await artDriver.DisconnectSingleConnectionAsync(outputNodeId, inputNodeId).ConfigureAwait(false);
                                                Debug.WriteLine($"[HandleClientAsync-Fallback] Hardware operation result: {result} for {inputNodeId}->{outputNodeId}");

                                                if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig cardConfig)
                                                {
                                                    var newState = state == 0 ? MeasureControl.Models.SwitchConnectionState.Connected : MeasureControl.Models.SwitchConnectionState.Disconnected;
                                                    cardConfig.SetConnection(inputNodeId, outputNodeId, newState);
                                                    try
                                                    {
                                                        _pxiChassisService?.UpdateDeviceCardConfig(targetSwitch.Id, cardConfig);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Debug.WriteLine($"[HandleClientAsync-Fallback] 更新设备配置失败: {ex.Message}");
                                                    }
                                                    // 通知有变化，便于 UI 层刷新（如果存在订阅者）
                                                    try
                                                    {
                                                        _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Publish(new MeasureControl.Events.DeviceModifiedEventArgs
                                                        {
                                                            ChassisName = this.ChassisName,
                                                            ModificationType = "RemoteCommand",
                                                            Device = targetSwitch
                                                        });
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                        else if (driverObj is MeasureControl.Drivers.PXI3022.PXI3022Driver pxi3022Driver)
                                        {
                                            // PXI-3022设备处理
                                            if (!pxi3022Driver.IsConnected)
                                            {
                                                var connected = await pxi3022Driver.ConnectAsync().ConfigureAwait(false);
                                                Debug.WriteLine($"[HandleClientAsync-Fallback] PXI3022Driver.ConnectAsync result: {connected}");
                                            }

                                            pxi3022Driver.DisconnectAsync();

                                            if (!pxi3022Driver.IsConnected)
                                            {
                                                // PXI3022的行列坐标转换逻辑
                                                if (int.TryParse(inputNodeId.TrimStart('r', 'R'), out int parsedInputIndex) &&
                                                    int.TryParse(outputNodeId.TrimStart('c', 'C'), out int parsedOutputIndex))
                                                {
                                                    // PXI3022 is 4 rows x 64 cols
                                                    int row = parsedInputIndex % 4;
                                                    int col = parsedOutputIndex % 64;
                                                    string channelId = $"R{row}C{col}";

                                                    bool result = false;
                                                    //if (state == 0)
                                                    //    result = await pxi3022Driver.WriteChannelAsync(channelId, 1.0).ConfigureAwait(false);
                                                    //else
                                                    //    result = await pxi3022Driver.WriteChannelAsync(channelId, 0.0).ConfigureAwait(false);


                                                    //if (state == 0)
                                                    //    pxi3022Driver.Write3022();
                                                    //else
                                                    //    pxi3022Driver.Write3022() ;

                                                    Debug.WriteLine($"[HandleClientAsync-Fallback] PXI3022 operation result: {result} for channel {channelId}");

                                                    // 更新配置和通知
                                                    if (targetSwitch.CardConfigData is MeasureControl.Models.SwitchMatrixCardConfig cardConfig)
                                                    {
                                                        var newState = state == 0 ? MeasureControl.Models.SwitchConnectionState.Connected : MeasureControl.Models.SwitchConnectionState.Disconnected;
                                                        cardConfig.SetConnection(inputNodeId, outputNodeId, newState);
                                                        try
                                                        {
                                                            _pxiChassisService?.UpdateDeviceCardConfig(targetSwitch.Id, cardConfig);
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Debug.WriteLine($"[HandleClientAsync-Fallback] 更新PXI3022设备配置失败: {ex.Message}");
                                                        }
                                                    }

                                                    // 通知有变化，便于 UI 层刷新（如果存在订阅者）
                                                    try
                                                    {
                                                        _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Publish(new MeasureControl.Events.DeviceModifiedEventArgs
                                                        {
                                                            ChassisName = this.ChassisName,
                                                            ModificationType = "RemoteCommand",
                                                            Device = targetSwitch
                                                        });
                                                    }
                                                    catch { }
                                                }
                                                else
                                                {
                                                    Debug.WriteLine($"[HandleClientAsync-Fallback] 无法解析PXI3022的行列坐标: {inputNodeId}, {outputNodeId}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[HandleClientAsync-Fallback] 异常: {ex.Message}");
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"[HandleClientAsync] 未找到对应槽位的 SwitchDevice: SlotIndex={slotIndex}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[HandleClientAsync] 处理命令异常: {ex.Message}");
                        }

                        var ack = cmd; // 回包与请求一致，供客户端做完整性校验
                        await stream.WriteAsync(ack, 0, ack.Length);
                        await stream.FlushAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HandleClientAsync] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 精确读取指定字节数的异步方法
        /// </summary>
        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count && !token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token);
                if (read <= 0)
                    break;

                totalRead += read;
            }

            return totalRead;
        }

        /// <summary>
        /// 根据槽位索引映射到对应的拓扑结构
        /// </summary>
        private static string TryMapSlotIndexToTopology(int slotIndex)
        {
            Debug.WriteLine($"[TryMapSlotIndexToTopology] Input slotIndex={slotIndex}");
            switch (slotIndex)
            {
                case 4:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 8x16 Matrix");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX;
                case 6:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 4x32 Matrix");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                case 7:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 4x32 Matrix");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                case 8:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 4x32 Matrix");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                case 9:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 4x32 Matrix");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                default:
                    Debug.WriteLine($"[TryMapSlotIndexToTopology] Slot {slotIndex} -> 4x32 Matrix (default)");
                    return MeasureControl.Drivers.ArtSwitch.artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
            }
        }

        #endregion

        /// <summary>
        /// 关闭在区域中的视图
        /// </summary>
        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭PXI机箱吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 构建完整的pageKey: PxiChassis_机箱名称
                string pageKey = $"PxiChassis_{ChassisName}";

                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 取消事件订阅
                _eventAggregator?.GetEvent<PxiChassisSelectedEvent>()?.Unsubscribe(OnPxiChassisSelected);
                _eventAggregator?.GetEvent<ProjectClosedEvent>()?.Unsubscribe(OnProjectClosed);
                _eventAggregator?.GetEvent<DeviceModifiedEvent>()?.Unsubscribe(OnDeviceModified);

                // 清理集合
                ChassisDevices?.Clear();
                AvailableChassis?.Clear();
                DeviceInfoItems?.Clear();
                Tools?.Clear();

                // 停止所有TCP服务器
                StopAllTcpServers();

                _disposed = true;
            }
        }

        #endregion

    }
}
