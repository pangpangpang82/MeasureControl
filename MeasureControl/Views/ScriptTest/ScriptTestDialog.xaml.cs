// ============================================================================
// 脚本测试对话框：选择脚本后立即开始执行，显示进度+日志，完成后可打开副本。
// 临时性外挂功能；如需关闭整个脚本测试能力，注释 App.xaml.cs 中 ScriptTestFeature.Register 一行即可。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Services.ScriptTest;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.Services.ScriptTest.Plugins;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Views.ScriptTest
{
    public partial class ScriptTestDialog : Window
    {
        private readonly IScriptTestService _service;
        private readonly IScriptTestPlugin _plugin;
        private readonly string _scriptPath;
        private CancellationTokenSource _cts;
        private string _resultPath;
        private bool _running;

        public ScriptTestDialog(IScriptTestService service, IScriptTestPlugin plugin, string scriptPath)
        {
            InitializeComponent();
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));

            var header = $"脚本测试 - {_plugin.DisplayName}";
            Title = header;
            TitleText.Text = header;
            ScriptPathText.Text = $"脚本: {_scriptPath}";
            Loaded += async (_, __) => await RunAsync();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { }
        }

        private void TitleCloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async Task RunAsync()
        {
            _cts = new CancellationTokenSource();
            _running = true;
            CancelButton.IsEnabled = true;
            CloseButton.IsEnabled = false;
            OpenResultButton.IsEnabled = false;

            AppendLog($"[{Now()}] 开始脚本测试…");

            ScriptTestRunSummary summary = null;
            try
            {
                summary = await Task.Run(() => _service.RunAsync(
                    _plugin,
                    _scriptPath,
                    msg => Dispatcher.Invoke(() => AppendLog($"[{Now()}] {msg}")),
                    msg => Dispatcher.Invoke(() => ProgressText.Text = msg),
                    _cts.Token)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog($"[{Now()}] 脚本测试发生未处理异常: {ex.Message}");
            }
            finally
            {
                _running = false;
                CancelButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
            }

            if (summary == null) return;

            // 加载阶段错误
            if (summary.LoadingIssues.Count > 0)
            {
                AppendLog("---- 脚本加载阶段错误 ----");
                foreach (var issue in summary.LoadingIssues)
                {
                    AppendLog("  " + issue);
                }
                AppendLog("脚本未进入测试。请修正后重试。");
                ProgressText.Text = "校验失败";
                OverallProgress.Value = 0;
                return;
            }

            OverallProgress.Value = 100;
            AppendLog("---- 测试结果汇总 ----");
            foreach (var r in summary.FcResults)
            {
                AppendLog($"  {r.TestId}: {r.ToCellText()}");
            }

            if (!string.IsNullOrEmpty(summary.ResultScriptPath) && File.Exists(summary.ResultScriptPath))
            {
                _resultPath = summary.ResultScriptPath;
                OpenResultButton.IsEnabled = true;
                AppendLog($"结果副本: {summary.ResultScriptPath}");
            }

            ProgressText.Text = summary.OverallPass ? "整体 PASS" : (summary.Cancelled ? "已取消" : "存在 FAIL/异常");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 立即置灰，防止重复点击（取消动作是不可逆的一次性操作）。
            // 最终在 RunAsync 的 finally 中按运行状态再统一设一次，这里的设置不会被反弹。
            CancelButton.IsEnabled = false;
            try { _cts?.Cancel(); } catch { }
            AppendLog("用户请求取消…（将在当前测试项可检查点生效）");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OpenResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_resultPath) || !File.Exists(_resultPath)) return;
            try { Process.Start(new ProcessStartInfo(_resultPath) { UseShellExecute = true }); } catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_running)
            {
                e.Cancel = true;
                ReMessageBox.Show("测试正在运行中，请先点击\"取消\"按钮停止测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            base.OnClosing(e);
        }

        private void AppendLog(string line)
        {
            LogText.AppendText(line + Environment.NewLine);
            LogScroller.ScrollToBottom();
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss");
    }
}
