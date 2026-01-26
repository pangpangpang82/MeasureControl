using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Helpers;
using MeasureControl.Views.Dialogs;
using Prism.Commands;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 重命名对话框的ViewModel
    /// </summary>
    public class RenameDialogViewModel : INotifyPropertyChanged
    {
        private string _oldName;
        private string _newName;
        private string _title;
        private string _errorMessage;
        private bool _hasError;
        private Func<string, bool> _validateFunc;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 对话框标题
        /// </summary>
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 原名称（只读显示）
        /// </summary>
        public string OldName
        {
            get => _oldName;
            set
            {
                _oldName = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 新名称（可编辑）
        /// </summary>
        public string NewName
        {
            get => _newName;
            set
            {
                _newName = value;
                OnPropertyChanged();
                ValidateName();
            }
        }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasError
        {
            get => _hasError;
            set
            {
                _hasError = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 对话框结果
        /// </summary>
        public bool? DialogResult { get; set; }

        /// <summary>
        /// 确定命令
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// 取消命令
        /// </summary>
        public ICommand CancelCommand { get; }

        public RenameDialogViewModel()
        {
            Title = "重命名";
            OkCommand = new DelegateCommand(ExecuteOk, CanExecuteOk);
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        /// <summary>
        /// 设置验证函数
        /// </summary>
        public void SetValidateFunc(Func<string, bool> validateFunc)
        {
            _validateFunc = validateFunc;
        }

        /// <summary>
        /// 验证名称
        /// </summary>
        private void ValidateName()
        {
            HasError = false;
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(NewName))
            {
                HasError = true;
                ErrorMessage = "名称不能为空";
                return;
            }

            // 如果新名称与旧名称相同，不算错误
            if (NewName == OldName)
            {
                return;
            }

            // 调用外部验证函数
            if (_validateFunc != null && !_validateFunc(NewName))
            {
                HasError = true;
                ErrorMessage = "该名称已存在，请使用其他名称";
            }
        }

        /// <summary>
        /// 是否可以执行确定命令
        /// </summary>
        private bool CanExecuteOk()
        {
            return !HasError && !string.IsNullOrWhiteSpace(NewName);
        }

        /// <summary>
        /// 执行确定命令
        /// </summary>
        private void ExecuteOk()
        {
            ValidateName();
            
            if (!HasError)
            {
                DialogResult = true;
                CloseWindow();
            }
        }

        /// <summary>
        /// 执行取消命令
        /// </summary>
        private void ExecuteCancel()
        {
            DialogResult = false;
            CloseWindow();
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void CloseWindow()
        {
            // 尝试从Application获取当前活动窗口
            var dialog = Application.Current.Windows.OfType<RenameDialog>().FirstOrDefault();
            if (dialog != null)
            {
                dialog.DialogResult = DialogResult;
                dialog.Close();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

