using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 测试启动进度对话框的 ViewModel。
    /// </summary>
    public class TestProgressDialogViewModel : BindableBase
    {
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
