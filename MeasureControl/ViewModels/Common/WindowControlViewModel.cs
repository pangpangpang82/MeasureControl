using System;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// 窗口控制ViewModel - 负责主窗口的窗口操作
    /// </summary>
    public class WindowControlViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IWindowManagerService _windowManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;

        private bool _isClosing = false;

        #endregion

        #region Commands

        public ICommand ToggleMinimizeCommand { get; private set; }
        public ICommand ToggleMaximizeCommand { get; private set; }
        public ICommand CloseMainWindowCommand { get; private set; }

        #endregion

        #region Constructor

        public WindowControlViewModel(
            IWindowManagerService windowManager,
            IEventAggregator eventAggregator,
            IDialogService dialogService)
        {
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCommands();
            SubscribeToEvents();
        }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            ToggleMinimizeCommand = new DelegateCommand(OnToggleMinimize);
            ToggleMaximizeCommand = new DelegateCommand(OnToggleMaximize);
            CloseMainWindowCommand = new DelegateCommand(OnCloseMainWindow);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<ApplicationClosingEvent>().Subscribe(OnApplicationClosing);
        }

        #endregion

        #region Command Implementations

        private void OnToggleMinimize()
        {
            try
            {
                _windowManager.MinimizeMainWindow();
            }
            catch (Exception)
            {
            }
        }

        private void OnToggleMaximize()
        {
            try
            {
                _windowManager.ToggleMaximizeMainWindow();
            }
            catch (Exception)
            {
            }
        }

        private void OnCloseMainWindow()
        {
            if (_isClosing) return;

            try
            {
                _isClosing = true;
                
                // 发布应用关闭事件
                _eventAggregator.GetEvent<ApplicationClosingEvent>().Publish();
                
                // 关闭主窗口
                _windowManager.CloseMainWindow();
            }
            catch (Exception)
            {
                _isClosing = false;
            }
        }

        #endregion

        #region Event Handlers

        private void OnApplicationClosing()
        {
            // 处理应用关闭前的清理工作
            _isClosing = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 检查是否可以关闭应用
        /// </summary>
        /// <returns>是否可以关闭</returns>
        public bool CanCloseApplication()
        {
            // 这里可以添加关闭前的检查逻辑
            // 例如：检查是否有未保存的数据
            return true;
        }

        /// <summary>
        /// 显示关闭确认对话框
        /// </summary>
        /// <returns>用户是否确认关闭</returns>
        public bool ShowCloseConfirmation()
        {
            return _dialogService.ShowConfirmationDialog("确定要关闭应用程序吗？", "确认关闭");
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 清理资源
                _isClosing = false;
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
