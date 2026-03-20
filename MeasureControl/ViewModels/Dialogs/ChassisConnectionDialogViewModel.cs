using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MeasureControl.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 机箱连接对话框ViewModel
    /// </summary>
    public class ChassisConnectionDialogViewModel : BindableBase
    {
        private ConnectionType _selectedConnectionType;
        private string _sourceChassisName;
        private string _targetChassisName;
        private bool _isDropdownOpen;
        private string _connectionName;
        private bool _isConnectionNameUserEdited = false;

        /// <summary>
        /// 源机箱名称
        /// </summary>
        public string SourceChassisName
        {
            get => _sourceChassisName;
            set => SetProperty(ref _sourceChassisName, value);
        }

        /// <summary>
        /// 目标机箱名称
        /// </summary>
        public string TargetChassisName
        {
            get => _targetChassisName;
            set => SetProperty(ref _targetChassisName, value);
        }

        /// <summary>
        /// 选中的连接类型项（用于ComboBox绑定）
        /// </summary>
        public ConnectionTypeItem SelectedConnectionTypeItem
        {
            get
            {
                return ConnectionTypes?.FirstOrDefault(x => x.Type == SelectedConnectionType);
            }
            set
            {
                if (value != null && SetProperty(ref _selectedConnectionType, value.Type))
                {
                    RaisePropertyChanged(nameof(SelectedConnectionTypeDisplayName));
                    // 如果用户没有手动编辑连接名称，则自动更新为连接方式
                    if (!_isConnectionNameUserEdited)
                    {
                        _connectionName = SelectedConnectionTypeDisplayName;
                        RaisePropertyChanged(nameof(ConnectionName));
                    }
                }
            }
        }

        /// <summary>
        /// 选中的连接类型
        /// </summary>
        public ConnectionType SelectedConnectionType
        {
            get => _selectedConnectionType;
            set
            {
                if (SetProperty(ref _selectedConnectionType, value))
                {
                    RaisePropertyChanged(nameof(SelectedConnectionTypeItem));
                    RaisePropertyChanged(nameof(SelectedConnectionTypeDisplayName));
                    // 如果用户没有手动编辑连接名称，则自动更新为连接方式
                    if (!_isConnectionNameUserEdited)
                    {
                        _connectionName = SelectedConnectionTypeDisplayName;
                        RaisePropertyChanged(nameof(ConnectionName));
                    }
                }
            }
        }

        /// <summary>
        /// 选中的连接类型显示名称
        /// </summary>
        public string SelectedConnectionTypeDisplayName
        {
            get
            {
                var item = ConnectionTypes?.FirstOrDefault(x => x.Type == SelectedConnectionType);
                return item?.DisplayName ?? "以太网连接";
            }
        }

        /// <summary>
        /// 可用的连接类型列表
        /// </summary>
        public List<ConnectionTypeItem> ConnectionTypes { get; }

        /// <summary>
        /// 下拉框是否打开
        /// </summary>
        public bool IsDropdownOpen
        {
            get => _isDropdownOpen;
            set
            {
                SetProperty(ref _isDropdownOpen, value);
                RaisePropertyChanged(nameof(TriangleSymbol));
            }
        }

        /// <summary>
        /// 三角形符号
        /// </summary>
        public string TriangleSymbol => IsDropdownOpen ? "▲" : "▼";

        /// <summary>
        /// 连接名称
        /// </summary>
        public string ConnectionName
        {
            get => _connectionName;
            set
            {
                if (SetProperty(ref _connectionName, value))
                {
                    _isConnectionNameUserEdited = true;
                    ConfirmCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 确认命令
        /// </summary>
        private readonly Func<string, bool> _isNameAvailable;
        public DelegateCommand ConfirmCommand { get; }

        /// <summary>
        /// 取消命令
        /// </summary>
        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 切换下拉框命令
        /// </summary>
        public DelegateCommand ToggleDropdownCommand { get; }

        /// <summary>
        /// 对话框结果
        /// </summary>
        public bool? DialogResult { get; set; }

        /// <summary>
        /// 确认事件
        /// </summary>
        public event Action<bool> DialogClosed;

        public ChassisConnectionDialogViewModel(string sourceChassisName, string targetChassisName, Func<string, bool> isNameAvailable = null)
        {
            SourceChassisName = sourceChassisName;
            TargetChassisName = targetChassisName;
            SelectedConnectionType = ConnectionType.Ethernet;
            // 默认连接名称为连接方式（以太网连接）
            _connectionName = "以太网连接";

            ConnectionTypes = new List<ConnectionTypeItem>
            {
                new ConnectionTypeItem { Type = ConnectionType.Ethernet, DisplayName = "以太网连接" },
                new ConnectionTypeItem { Type = ConnectionType.USB, DisplayName = "USB连接" },
                new ConnectionTypeItem { Type = ConnectionType.Serial, DisplayName = "串口连接" }
            };
            _isNameAvailable = isNameAvailable;
            ConfirmCommand = new DelegateCommand(OnConfirm, CanConfirm);
            CancelCommand = new DelegateCommand(OnCancel);
            ToggleDropdownCommand = new DelegateCommand(OnToggleDropdown);
        }

        private void OnConfirm()
        {
            ConnectionName = ConnectionName?.Trim();
            DialogResult = true;
            DialogClosed?.Invoke(true);
        }

        private bool CanConfirm()
        {
            if (string.IsNullOrWhiteSpace(ConnectionName)) return false;
            if (_isNameAvailable != null)
            {
                try
                {
                    return _isNameAvailable(ConnectionName);
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private void OnCancel()
        {
            DialogResult = false;
            DialogClosed?.Invoke(false);
        }

        private void OnToggleDropdown()
        {
            IsDropdownOpen = !IsDropdownOpen;
        }
    }

    /// <summary>
    /// 连接类型项
    /// </summary>
    public class ConnectionTypeItem
    {
        public ConnectionType Type { get; set; }
        public string DisplayName { get; set; }
    }
}
