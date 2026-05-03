// ============================================================================
// 脚本测试功能注册入口（临时性甲方需求）。
// 注释掉 App.xaml.cs 中 ScriptTestFeature.Register(containerRegistry) 一行即可彻底关闭脚本测试功能。
// 关闭后：右键菜单不会出现"脚本测试…"，IoC 不会注册任何脚本测试相关服务。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MeasureControl.Events;
using MeasureControl.Services.ScriptTest;
using MeasureControl.Services.ScriptTest.Plugins;
using MeasureControl.Views.ScriptTest;
using Microsoft.Win32;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.Helpers.ScriptTest
{
    public static class ScriptTestFeature
    {
        /// <summary>
        /// 已注册的脚本测试插件（BoardType → Plugin）。
        /// 新增板型：实现 IScriptTestPlugin → 在 Register 的 Plugins 列表中追加一项即可。
        /// </summary>
        private static readonly Dictionary<string, IScriptTestPlugin> Plugins
            = new Dictionary<string, IScriptTestPlugin>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 在 IoC 注册脚本测试相关服务，并订阅板卡右键菜单构建事件。
        /// </summary>
        public static void Register(IContainerRegistry containerRegistry)
        {
            if (containerRegistry == null) throw new ArgumentNullException(nameof(containerRegistry));

            // ---- 注册支持脚本测试的板型插件。新增板型在此追加即可。 ----
            RegisterPlugin(new FuelControllerScriptTestPlugin());
            RegisterPlugin(new HydraulicControllerScriptTestPlugin());

            // 无状态调度器，所有板型共用同一单例。
            containerRegistry.RegisterSingleton<IScriptTestService, ScriptTestService>();

            // 同步订阅事件：RegisterTypes 阶段 ContainerLocator 已就绪 + IEventAggregator 已由 Prism 预注册。
            // 注意必须传 keepSubscriberReferenceAlive=true，否则静态方法的 Action 委托会被 GC（PubSubEvent 默认弱引用）。
            try
            {
                var ea = ContainerLocator.Container.Resolve<IEventAggregator>();
                // 必须用 PublisherThread（同步）：Publish 本就在 UI 线程，且发布方在 Publish 返回后
                // 立即遍历 MenuItems 列表注入菜单项；若用 UIThread 会异步 Post 到 dispatcher 队列，
                // 导致 OnBuildingMenu 尚未执行，MenuItems 为空，菜单看不到"脚本测试…"。
                ea?.GetEvent<BoardContextMenuBuildingEvent>()?.Subscribe(
                    OnBuildingMenu,
                    ThreadOption.PublisherThread,
                    keepSubscriberReferenceAlive: true);
                System.Diagnostics.Debug.WriteLine("[ScriptTestFeature] 已订阅 BoardContextMenuBuildingEvent");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptTestFeature] 订阅菜单事件失败: {ex.Message}");
            }
        }

        private static void RegisterPlugin(IScriptTestPlugin plugin)
        {
            if (plugin == null) return;
            if (string.IsNullOrEmpty(plugin.BoardType)) return;
            Plugins[plugin.BoardType] = plugin;
        }

        private static void OnBuildingMenu(BoardContextMenuBuildingEventArgs args)
        {
            if (args == null || args.MenuItems == null) return;
            if (string.IsNullOrEmpty(args.BoardType)) return;
            if (!Plugins.TryGetValue(args.BoardType, out var plugin)) return;

            var item = new MenuItem { Header = "脚本测试" };
            if (args.MenuItemStyle != null) item.Style = args.MenuItemStyle;

            item.Click += (s, e) =>
            {
                try
                {
                    LaunchScriptTest(plugin);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"启动脚本测试失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            args.MenuItems.Add(item);
        }

        private static void LaunchScriptTest(IScriptTestPlugin plugin)
        {
            var dlg = new OpenFileDialog
            {
                Title = $"选择{plugin.DisplayName}脚本文件",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false,
            };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            var service = ContainerLocator.Container.Resolve<IScriptTestService>();
            var window = new ScriptTestDialog(service, plugin, dlg.FileName)
            {
                Owner = Application.Current?.MainWindow,
            };
            window.ShowDialog();
        }
    }
}
