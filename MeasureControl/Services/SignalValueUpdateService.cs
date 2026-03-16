using System;
using System.Diagnostics;
using Prism.Events;
using MeasureControl.ViewModels.TestTask.ConfigTabel;

namespace MeasureControl.Services
{
    /// <summary>
    /// 信号数值更新服务
    /// 负责监听硬件数值变化并更新SignalConfigItem
    /// </summary>
    public class SignalValueUpdateService : IDisposable
    {
        private readonly IEventAggregator _eventAggregator;
        private bool _isDisposed = false;
        private Models.ChassisModel _currentRunningChassis;
        private Models.ProjectItem _currentRunningTestTask;

        public SignalValueUpdateService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            // 订阅硬件数值变化事件
            var hardwareService = HardwareControlService.Instance;
            hardwareService.VariableValueChanged += OnVariableValueChanged;

            Debug.WriteLine("[SignalValueUpdateService] 已初始化并订阅数值变化事件");
        }

        /// <summary>
        /// 设置当前运行的机箱和测试任务上下文
        /// </summary>
        /// <param name="chassis">机箱对象</param>
        /// <param name="testTask">测试任务对象</param>
        public void SetRunningContext(Models.ChassisModel chassis, Models.ProjectItem testTask)
        {
            _currentRunningChassis = chassis;
            _currentRunningTestTask = testTask;
            Debug.WriteLine($"[SignalValueUpdateService] 运行上下文已设置: Chassis={chassis?.Name}, TestTask={testTask?.Name}");
        }

        /// <summary>
        /// 处理变量数值变化事件
        /// </summary>
        private void OnVariableValueChanged(object sender, VariableValueChangedEventArgs e)
        {
            try
            {
                // 在UI线程更新SignalConfigItem
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    UpdateSignalTabelValues(e.VariablePath, e.Value);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SignalValueUpdateService] 更新信号数值失败 {e.VariablePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新信号配置表数值
        /// </summary>
        /// <param name="variablePath">变量路径，格式："变量表名:变量名"</param>
        /// <param name="value">新的数值</param>
        private void UpdateSignalTabelValues(string variablePath, double value)
        {
            // 解析变量路径格式: "变量表名:变量名"
            var parts = variablePath.Split(':');
            if (parts.Length != 2)
            {
                Debug.WriteLine($"[SignalValueUpdateService] 无效的变量路径格式: {variablePath}");
                return;
            }

            string variableTabelName = parts[0];
            string variableName = parts[1];

            // 构建完整的tabelKey: "机箱名/测试任务名/变量表名" 或 "测试任务名/变量表名"
            string tabelKey = null;
            if (_currentRunningChassis != null && _currentRunningTestTask != null)
            {
                tabelKey = $"{_currentRunningChassis.Name}/{_currentRunningTestTask.Name}/{variableTabelName}";
            }
            else if (_currentRunningTestTask != null)
            {
                tabelKey = $"{_currentRunningTestTask.Name}/{variableTabelName}";
            }
            else
            {
                Debug.WriteLine($"[SignalValueUpdateService] 无法确定运行上下文，无法更新信号值: {variablePath}");
                return;
            }

            // 更新SignalConfigItem
            bool updated = SignalConfigTabelViewModel.UpdateSignalValue(tabelKey, variableName, value);

            if (updated)
            {
                Debug.WriteLine($"[SignalValueUpdateService] 更新信号 {variablePath} = {value}");
            }
            else
            {
                Debug.WriteLine($"[SignalValueUpdateService] 信号更新失败: {variablePath}");
            }
        }

        /// <summary>
        /// 更新信号配置表数值（支持指定机箱和测试任务）
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <param name="testTaskName">测试任务名称</param>
        /// <param name="variablePath">变量路径</param>
        /// <param name="value">数值</param>
        public void UpdateSignalValue(string chassisName, string testTaskName, string variablePath, double value)
        {
            var parts = variablePath.Split(':');
            if (parts.Length != 2) return;

            string variableTabelName = parts[0];
            string variableName = parts[1];

            // 构建完整的表键
            string tabelKey = $"{chassisName}/{testTaskName}/{variableTabelName}";

            bool updated = SignalConfigTabelViewModel.UpdateSignalValue(tabelKey, variableName, value);

            if (updated)
            {
                Debug.WriteLine($"[SignalValueUpdateService] 更新信号 {tabelKey}:{variableName} = {value}");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                // 取消事件订阅
                var hardwareService = HardwareControlService.Instance;
                hardwareService.VariableValueChanged -= OnVariableValueChanged;

                _isDisposed = true;
                Debug.WriteLine("[SignalValueUpdateService] 已释放");
            }
        }
    }
}
