using System;
using System.Collections.Generic;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels
{
    public class LoginViewModel : BindableBase
    {
        private string _userId;
        public string UserId
        {
            get => _userId;
            set
            {
                if (SetProperty(ref _userId, value))
                {
                    UpdatePermission();
                    RaiseLoginCanExecuteChanged();
                }
            }
        }

        private string _password;
        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    RaiseLoginCanExecuteChanged();
                }
            }
        }

        private string _permission = "未知";
        public string Permission
        {
            get => _permission;
            set => SetProperty(ref _permission, value);
        }

        private bool _isDropdownOpen;
        public bool IsDropdownOpen
        {
            get => _isDropdownOpen;
            set
            {
                if (SetProperty(ref _isDropdownOpen, value))
                {
                    RaisePropertyChanged(nameof(TriangleSymbol));
                }
            }
        }

        public string TriangleSymbol => IsDropdownOpen ? "▲" : "▼";

        private bool _isPasswordVisible = false;
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                if (SetProperty(ref _isPasswordVisible, value))
                {
                    RaisePropertyChanged(nameof(PasswordToggleImagePath));
                }
            }
        }

        public string PasswordToggleImagePath => IsPasswordVisible ? "/Resources/Logo/visual.png" : "/Resources/Logo/unvisual.png";

        private List<string> _uidOptions = new List<string> { "20231001", "20231002", "20231003", "20231004", "20231005" };
        public List<string> UidOptions
        {
            get => _uidOptions;
            set => SetProperty(ref _uidOptions, value);
        }

        public event Action LoginSuccess;
        public event Action<string> ShowMessageRequested;
        public event Action RequestClose;


        public ICommand LoginCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }
        public ICommand ShowDropdownUidCommand { get; private set; }
        public ICommand TogglePasswordVisibilityCommand { get; private set; }

        public LoginViewModel()
        {
            LoginCommand = new DelegateCommand(Login, CanLogin);
            CloseCommand = new DelegateCommand(CloseWindow);
            ShowDropdownUidCommand = new DelegateCommand(ShowDropdown);
            TogglePasswordVisibilityCommand = new DelegateCommand(TogglePasswordVisibility);
        }

        private void Login()
        {
            // 验证用户信息
            if (UserId == "admin" && Password == "123")
            {
                Permission = "管理员";
                LoginSuccess?.Invoke();
            }
            else
            {
                Permission = "未知";
                ShowMessageRequested?.Invoke("账号或密码错误！");
            }
        }

        private void CloseWindow()
        {
            RequestClose?.Invoke();
        }

        private void ShowDropdown()
        {
            IsDropdownOpen = !IsDropdownOpen;
        }

        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        private void UpdatePermission()
        {
            if (string.IsNullOrEmpty(_userId))
            {
                Permission = "未知";
            }
            else if (_userId == "admin")
            {
                Permission = "管理员";
            }
            else
            {
                Permission = "普通用户";
            }
        }

        private bool CanLogin()
        {
            return !string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(Password);
        }

        private void RaiseLoginCanExecuteChanged()
        {
            if (LoginCommand is DelegateCommand delegateCommand)
            {
                delegateCommand.RaiseCanExecuteChanged();
            }
        }

    }
}
