using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 添加机箱对话框ViewModel
    /// </summary>
    public class AddChassisDialogViewModel : BindableBase
    {
        private string _chassisName;
        private string _ipAddress;
        private string _chassisModel;
        private string _ipAddressError;
        private string _subnetMask;
        private string _subnetMaskError;
        private LocalNetworkInterfaceInfo _selectedNetworkInterface;

        /// <summary>
        /// 本地网口列表
        /// </summary>
        public ObservableCollection<LocalNetworkInterfaceInfo> NetworkInterfaces { get; } = new ObservableCollection<LocalNetworkInterfaceInfo>();

        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set
            {
                if (SetProperty(ref _chassisName, value))
                {
                    ConfirmCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                {
                    ValidateIpAddress();
                    ValidateSubnetMask();
                    ConfirmCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// IP地址错误提示
        /// </summary>
        public string IpAddressError
        {
            get => _ipAddressError;
            set => SetProperty(ref _ipAddressError, value);
        }

        /// <summary>
        /// 子网掩码
        /// </summary>
        public string SubnetMask
        {
            get => _subnetMask;
            set
            {
                if (SetProperty(ref _subnetMask, value))
                {
                    ValidateSubnetMask();
                    ConfirmCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 子网掩码错误提示
        /// </summary>
        public string SubnetMaskError
        {
            get => _subnetMaskError;
            set => SetProperty(ref _subnetMaskError, value);
        }

        /// <summary>
        /// 选中的本地网口
        /// </summary>
        public LocalNetworkInterfaceInfo SelectedNetworkInterface
        {
            get => _selectedNetworkInterface;
            set
            {
                if (SetProperty(ref _selectedNetworkInterface, value) && value != null)
                {
                    // 切换网口时自动填充 IP 和子网掩码
                    if (!string.IsNullOrWhiteSpace(value.IpAddress))
                    {
                        IpAddress = value.IpAddress;
                    }
                    if (!string.IsNullOrWhiteSpace(value.SubnetMask))
                    {
                        SubnetMask = value.SubnetMask;
                    }
                }
            }
        }

        /// <summary>
        /// 机箱型号
        /// </summary>
        public string ChassisModel
        {
            get => _chassisModel;
            set => SetProperty(ref _chassisModel, value);
        }

        /// <summary>
        /// 确认命令
        /// </summary>
        public DelegateCommand ConfirmCommand { get; }

        /// <summary>
        /// 取消命令
        /// </summary>
        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 对话框结果
        /// </summary>
        public bool? DialogResult { get; private set; }

        /// <summary>
        /// 对话框关闭事件
        /// </summary>
        public event Action<bool> DialogClosed;

        public AddChassisDialogViewModel(string chassisModel, string defaultChassisName)
        {
            _chassisModel = chassisModel;
            
            // 使用服务生成的默认机箱名称（如"PXI机箱1"）
            _chassisName = defaultChassisName;
            _ipAddress = "127.0.0.1";  // 默认本地地址
            _subnetMask = "255.255.255.0"; // 默认子网掩码

            ConfirmCommand = new DelegateCommand(OnConfirm, CanConfirm);
            CancelCommand = new DelegateCommand(OnCancel);

            // 构造函数中枚举本地网口
            LoadNetworkInterfaces();
        }

        private void OnConfirm()
        {
            ChassisName = ChassisName?.Trim();
            IpAddress = IpAddress?.Trim();
            SubnetMask = SubnetMask?.Trim();
            DialogResult = true;
            DialogClosed?.Invoke(true);
        }

        private bool CanConfirm()
        {
            return !string.IsNullOrWhiteSpace(ChassisName) && 
                   !string.IsNullOrWhiteSpace(IpAddress) && 
                   !string.IsNullOrWhiteSpace(SubnetMask) &&
                   IsValidIpAddress(IpAddress) &&
                   IsValidSubnetMask(SubnetMask);
        }

        private void OnCancel()
        {
            DialogResult = false;
            DialogClosed?.Invoke(false);
        }

        /// <summary>
        /// 验证IP地址并设置错误提示
        /// </summary>
        private void ValidateIpAddress()
        {
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                IpAddressError = "请输入IP地址";
            }
            else if (!IsValidIpAddress(IpAddress))
            {
                IpAddressError = "IP地址格式不正确";
            }
            else
            {
                IpAddressError = null;
            }
        }

        /// <summary>
        /// 验证子网掩码并设置错误提示
        /// </summary>
        private void ValidateSubnetMask()
        {
            if (string.IsNullOrWhiteSpace(SubnetMask))
            {
                SubnetMaskError = "请输入子网掩码";
            }
            else if (!IsValidSubnetMask(SubnetMask))
            {
                SubnetMaskError = "子网掩码格式不正确";
            }
            else
            {
                SubnetMaskError = null;
            }
        }

        /// <summary>
        /// 验证IP地址格式是否正确
        /// </summary>
        private bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // 使用正则表达式验证IPv4地址格式
            string pattern = @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$";
            
            if (!Regex.IsMatch(ipAddress.Trim(), pattern))
                return false;

            // 额外使用 IPAddress.TryParse 进行验证
            return IPAddress.TryParse(ipAddress.Trim(), out _);
        }

        /// <summary>
        /// 验证子网掩码格式（简单按IPv4格式校验）
        /// </summary>
        private bool IsValidSubnetMask(string subnetMask)
        {
            if (string.IsNullOrWhiteSpace(subnetMask))
                return false;

            string pattern = @"^((255|254|252|248|240|224|192|128|0)\.){3}(255|254|252|248|240|224|192|128|0)$";

            if (!Regex.IsMatch(subnetMask.Trim(), pattern))
                return false;

            return IPAddress.TryParse(subnetMask.Trim(), out _);
        }

        /// <summary>
        /// 枚举本地可用网口，并填充 NetworkInterfaces 列表
        /// </summary>
        private void LoadNetworkInterfaces()
        {
            try
            {
                NetworkInterfaces.Clear();

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 只考虑状态为 Up 的有 IPv4 地址的接口
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                            continue;

                        var info = new LocalNetworkInterfaceInfo
                        {
                            Name = ni.Name,
                            Description = ni.Description,
                            IpAddress = ua.Address.ToString(),
                            SubnetMask = ua.IPv4Mask?.ToString() ?? string.Empty
                        };

                        NetworkInterfaces.Add(info);
                    }
                }

                // 默认选中第一个网口
                if (NetworkInterfaces.Count > 0)
                {
                    SelectedNetworkInterface = NetworkInterfaces[0];
                }
            }
            catch (Exception)
            {
                // 枚举失败时忽略，让用户手动输入 IP/子网掩码
            }
        }

        /// <summary>
        /// 本地网口信息模型
        /// </summary>
        public class LocalNetworkInterfaceInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string IpAddress { get; set; }
            public string SubnetMask { get; set; }

            public string DisplayName => string.IsNullOrWhiteSpace(IpAddress)
                ? Name
                : $"{Name} - {IpAddress}";
        }
    }
}
