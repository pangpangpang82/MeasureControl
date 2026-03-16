using System;
using System.Windows;

namespace MeasureControl.Services
{
    /// <summary>
    /// 项目保存状态管理服务实现
    /// </summary>
    public class ProjectSaveStateService : IProjectSaveStateService
    {
        private bool _hasUnsavedChanges;
        private readonly IDialogService _dialogService;

        public ProjectSaveStateService(IDialogService dialogService)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _hasUnsavedChanges = false;
        }

        /// <summary>
        /// 项目是否有未保存的更改
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    SaveStateChanged?.Invoke(this, _hasUnsavedChanges);
                }
            }
        }

        /// <summary>
        /// 项目保存状态改变事件
        /// </summary>
        public event EventHandler<bool> SaveStateChanged;

        /// <summary>
        /// 标记项目为已修改
        /// </summary>
        public void MarkAsModified()
        {
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// 标记项目为已保存
        /// </summary>
        public void MarkAsSaved()
        {
            HasUnsavedChanges = false;
        }

        /// <summary>
        /// 重置保存状态
        /// </summary>
        public void Reset()
        {
            HasUnsavedChanges = false;
        }

        /// <summary>
        /// 检查是否可以安全关闭项目
        /// </summary>
        /// <returns>如果可以安全关闭返回true，否则返回false</returns>
        public bool CanCloseSafely()
        {
            if (!HasUnsavedChanges)
            {
                return true;
            }

            // 显示保存确认对话框
            var result = _dialogService.ShowConfirmDialog(
                "项目有未保存的更改，是否要保存项目？",
                "保存确认");

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // 用户选择保存，返回false让调用者处理保存逻辑
                    return false;
                case MessageBoxResult.No:
                    // 用户选择不保存，可以安全关闭
                    return true;
                default:
                    // 默认情况，不能关闭
                    return false;
            }
        }
    }
}
