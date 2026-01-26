using System;
using System.Windows.Threading;
using MeasureControl.Helpers;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// 时间管理ViewModel - 负责时间显示和更新
    /// </summary>
    public class TimeManagementViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private string _currentTime;
        private DispatcherTimer _timer;
        private bool _isDisposed = false;

        #endregion

        #region Public Properties

        /// <summary>
        /// 当前时间显示
        /// </summary>
        public string CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        #endregion

        #region Constructor

        public TimeManagementViewModel()
        {
            InitializeTimer();
        }

        #endregion

        #region Private Methods

        private void InitializeTimer()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            // 立即更新一次时间
            UpdateTime();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_isDisposed)
            {
                UpdateTime();
            }
        }

        private void UpdateTime()
        {
            try
            {
                CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 启动时间更新
        /// </summary>
        public void StartTimeUpdater()
        {
            if (_timer != null && !_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        /// <summary>
        /// 停止时间更新
        /// </summary>
        public void StopTimeUpdater()
        {
            if (_timer != null && _timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        /// <summary>
        /// 获取格式化的当前时间
        /// </summary>
        /// <param name="format">时间格式</param>
        /// <returns>格式化的时间字符串</returns>
        public string GetFormattedTime(string format = "yyyy-MM-dd HH:mm:ss")
        {
            try
            {
                return DateTime.Now.ToString(format);
            }
            catch (Exception)
            {
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // 使用 ResourceCleanupHelper 清理 DispatcherTimer
                    ResourceCleanupHelper.DisposeTimer(ref _timer, OnTimerTick);
                }

                _isDisposed = true;
            }
        }

        ~TimeManagementViewModel()
        {
            Dispose(false);
        }

        #endregion
    }
}
