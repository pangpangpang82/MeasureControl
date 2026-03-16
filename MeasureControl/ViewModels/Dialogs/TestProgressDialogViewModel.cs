using Prism.Mvvm;
using System;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 测试启动进度对话框的 ViewModel。
    /// </summary>
    public class TestProgressDialogViewModel : BindableBase
    {
        public Action RequestCancel { get; set; }

        private bool _confirmStopOnClose = true;
        public bool ConfirmStopOnClose
        {
            get => _confirmStopOnClose;
            set => SetProperty(ref _confirmStopOnClose, value);
        }

        private string _headerText = "配置中";
        public string HeaderText
        {
            get => _headerText;
            set => SetProperty(ref _headerText, value);
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private int _total = 100;
        public int Total
        {
            get => _total;
            set => SetProperty(ref _total, value);
        }

        private string _statusText = "正在配置设备...";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        private bool _isFailed;
        public bool IsFailed
        {
            get => _isFailed;
            set => SetProperty(ref _isFailed, value);
        }

        private string _errorText;
        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }
    }
}
