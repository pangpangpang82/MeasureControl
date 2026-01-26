using System.Collections.ObjectModel;
using MeasureControl.Models;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Hardware
{
    /// <summary>
    /// 设备信息面板的ViewModel
    /// </summary>
    public class DeviceInfoViewModel : BindableBase
    {
        private ObservableCollection<DeviceInfoItem> _deviceInfoItems;

        /// <summary>
        /// 设备信息项集合
        /// </summary>
        public ObservableCollection<DeviceInfoItem> DeviceInfoItems
        {
            get => _deviceInfoItems;
            set => SetProperty(ref _deviceInfoItems, value);
        }

        public DeviceInfoViewModel()
        {
            DeviceInfoItems = new ObservableCollection<DeviceInfoItem>();
        }

        /// <summary>
        /// 使用指定的设备信息项初始化ViewModel
        /// </summary>
        public DeviceInfoViewModel(ObservableCollection<DeviceInfoItem> deviceInfoItems)
        {
            DeviceInfoItems = deviceInfoItems;
        }
    }
}

