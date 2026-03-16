using System;
using System.Windows;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class ReMessageBoxViewModel : BindableBase
    {
        #region Private Fields

        private string _message;
        private string _title;
        private MessageBoxButton _buttons;
        private MessageBoxImage _image;
        private MessageBoxResult _result;

        #endregion

        #region Properties

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public MessageBoxButton Buttons
        {
            get => _buttons;
            set => SetProperty(ref _buttons, value);
        }

        public MessageBoxImage Image
        {
            get => _image;
            set => SetProperty(ref _image, value);
        }

        public MessageBoxResult Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
        }

        // 按钮可见性属性
        public bool IsOkButtonVisible => Buttons == MessageBoxButton.OK || 
                                        Buttons == MessageBoxButton.OKCancel;

        public bool IsYesButtonVisible => Buttons == MessageBoxButton.YesNo || 
                                         Buttons == MessageBoxButton.YesNoCancel;

        public bool IsNoButtonVisible => Buttons == MessageBoxButton.YesNo || 
                                        Buttons == MessageBoxButton.YesNoCancel;

        public bool IsCancelButtonVisible => Buttons == MessageBoxButton.OKCancel || 
                                            Buttons == MessageBoxButton.YesNoCancel;

        // 图标路径
        public string IconPath
        {
            get
            {
                switch (Image)
                {
                    case MessageBoxImage.Information:
                        return "/Resources/MessageBox/info.png";
                    case MessageBoxImage.Warning:
                        return "/Resources/MessageBox/warning.png";
                    case MessageBoxImage.Error:
                        return "/Resources/MessageBox/error.png";
                    case MessageBoxImage.Question:
                        return "/Resources/MessageBox/question.png";
                    default:
                        return null;
                }
            }
        }

        public bool IsIconVisible => !string.IsNullOrEmpty(IconPath);

        #endregion

        #region Commands

        public ICommand YesCommand { get; private set; }
        public ICommand NoCommand { get; private set; }
        public ICommand OkCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        #endregion

        #region Events

        public event Action<MessageBoxResult> ResultSelected;
        public event Action RequestClose;

        #endregion

        #region Constructor

        public ReMessageBoxViewModel()
        {
            InitializeCommands();
        }

        public ReMessageBoxViewModel(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
            : this()
        {
            Message = message;
            Title = title;
            Buttons = buttons;
            Image = image;
        }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            YesCommand = new DelegateCommand(() => SetResult(MessageBoxResult.Yes));
            NoCommand = new DelegateCommand(() => SetResult(MessageBoxResult.No));
            OkCommand = new DelegateCommand(() => SetResult(MessageBoxResult.OK));
            CancelCommand = new DelegateCommand(() => SetResult(MessageBoxResult.Cancel));
        }

        /// <summary>
        /// 设置结果并关闭对话框
        /// </summary>
        public void SetResult(MessageBoxResult result)
        {
            Result = result;
            ResultSelected?.Invoke(result);
            RequestClose?.Invoke();
        }

        #endregion

        #region Public Methods

        public void HandleKeyDown(Key key)
        {
            switch (key)
            {
                case Key.Enter:
                    // Enter键确认当前焦点按钮或默认按钮
                    ExecuteDefaultButton();
                    break;
                case Key.Escape:
                    // Esc键取消
                    if (IsCancelButtonVisible)
                    {
                        SetResult(MessageBoxResult.Cancel);
                    }
                    else if (IsNoButtonVisible)
                    {
                        SetResult(MessageBoxResult.No);
                    }
                    else
                    {
                        SetResult(MessageBoxResult.OK);
                    }
                    break;
            }
        }

        private void ExecuteDefaultButton()
        {
            if (IsYesButtonVisible)
            {
                SetResult(MessageBoxResult.Yes);
            }
            else if (IsOkButtonVisible)
            {
                SetResult(MessageBoxResult.OK);
            }
            else if (IsNoButtonVisible)
            {
                SetResult(MessageBoxResult.No);
            }
            else if (IsCancelButtonVisible)
            {
                SetResult(MessageBoxResult.Cancel);
            }
        }

        #endregion
    }
}
